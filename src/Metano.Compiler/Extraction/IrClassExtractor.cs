using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts an <see cref="IrClassDeclaration"/> from a Roslyn class/struct/record symbol.
/// <para>
/// <b>Scope:</b> Emits the type header (name, visibility, semantics, base type, interfaces,
/// type parameters, attributes), member signatures (fields, properties, methods, events
/// with semantic flags), and constructor shapes (primary + overloads with parameter
/// promotion flags). Method bodies, property getter/setter bodies, initializer
/// expressions, and constructor bodies are left null and filled in when expression
/// extraction is available. Nested types are <em>not</em> extracted here; each
/// language target collects runtime requirements for the nested companion variants
/// it emits (the TypeScript target does so in <c>TypeTransformer.ScanIrRuntimeRequirements</c>).
/// </para>
/// </summary>
public static class IrClassExtractor
{
    public static IrClassDeclaration Extract(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver = null,
        Compilation? compilation = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        var semantics = ExtractSemantics(type, originResolver);

        var baseType =
            type.BaseType is not null && IsMeaningfulBase(type.BaseType)
                ? IrTypeRefMapper.Map(type.BaseType, originResolver)
                : null;

        var emittedInterfaces = SymbolHelper.EmittedInterfaces(type, target);

        var interfaces =
            emittedInterfaces.Count > 0
                ? emittedInterfaces.Select(i => IrTypeRefMapper.Map(i, originResolver)).ToList()
                : null;

        var typeParameters =
            type.TypeParameters.Length > 0
                ? type
                    .TypeParameters.Select(tp => ExtractTypeParameter(tp, originResolver))
                    .ToList()
                : null;

        // Detect primary-constructor parameters captured implicitly by
        // member bodies. Walk the type's method/property syntax once
        // BEFORE extracting members: the captured-param map drives an
        // identifier-rewrite inside `IrExpressionExtractor` so member
        // bodies see `this._field` instead of an out-of-scope param
        // binding. The map and its synthesized fields are scoped to
        // this type's extraction via an `AsyncLocal`.
        var deferInitializers = PrimaryConstructorInitializerDeferral.AppliesTo(type);
        var captureScan = ImplicitCaptureDetector.Scan(
            type,
            compilation,
            originResolver,
            deferInitializers
        );

        var members = ExtractWithCaptureContext(
            captureScan.ParamFieldMap,
            deferInitializers,
            () => ExtractMembers(type, originResolver, compilation, target)
        );
        members.AddRange(captureScan.SynthesizedFields);

        var constructor = IrConstructorExtractor.Extract(type, originResolver, compilation, target);

        // Mark constructor parameters that are captured by a field
        // initializer (DI-style: `private readonly IFoo _foo = foo;`). The
        // extractor needs both halves to be available — the field IR for the
        // identifier match, the constructor IR for the parameter slot — so
        // it runs as a post-processing pass after the regular member /
        // constructor extraction completes.
        constructor = AnnotateCapturedParams(constructor, members);

        // Annotate constructor params synthesized by the implicit-capture
        // scan. The detector already paired each param with the right
        // field name; surface that on the IR so the backend's ctor body
        // emitter assigns the field the same way it does for explicit
        // captures.
        constructor = AnnotateImplicitCaptures(constructor, captureScan.ParamFieldMap);

        // Initializers that read a primary-constructor parameter move into
        // the constructor body, where the parameter is in scope. Runs after
        // both capture annotations: reads of a captured field trigger it.
        if (deferInitializers)
        {
            var deferral = PrimaryConstructorInitializerDeferral.Apply(
                type,
                members,
                constructor,
                target
            );

            members = deferral.Members;
            constructor = deferral.Constructor;
        }

        return new IrClassDeclaration(
            type.Name,
            IrVisibilityMapper.Map(type.DeclaredAccessibility),
            semantics,
            BaseType: baseType,
            Interfaces: interfaces,
            Members: members.Count > 0 ? members : null,
            Constructor: constructor,
            NestedTypes: null,
            TypeParameters: typeParameters,
            Attributes: IrAttributeExtractor.Extract(type),
            EqualityMembers: IrRecordEqualityMemberExtractor.Extract(type, originResolver, target)
        );
    }

    private static T ExtractWithCaptureContext<T>(
        IReadOnlyDictionary<ISymbol, string>? captureMap,
        bool initializersRunInConstructor,
        Func<T> extract
    )
    {
        if (captureMap is null || captureMap.Count == 0)
            return extract();

        var previous = IrExpressionExtractor.PrimaryCtorScope;
        IrExpressionExtractor.PrimaryCtorScope = new PrimaryCtorExtractionScope(
            captureMap,
            initializersRunInConstructor
        );

        try
        {
            return extract();
        }
        finally
        {
            IrExpressionExtractor.PrimaryCtorScope = previous;
        }
    }

    /// <summary>
    /// Surfaces the implicit-capture map produced by
    /// <see cref="ImplicitCaptureDetector"/> on the constructor IR.
    /// The detector pairs each captured param with the synthesized
    /// backing field's name; mirroring that on
    /// <see cref="IrConstructorParameter.CapturedFieldName"/> lets
    /// the backend emit the <c>this._field = param</c> assignment
    /// using the same code path it uses for explicit captures.
    /// </summary>
    private static IrConstructorDeclaration? AnnotateImplicitCaptures(
        IrConstructorDeclaration? ctor,
        IReadOnlyDictionary<ISymbol, string>? captureMap
    )
    {
        if (ctor is null || captureMap is null || captureMap.Count == 0)
            return ctor;

        var byParamName = captureMap
            .Keys.OfType<IParameterSymbol>()
            .ToDictionary(p => p.Name, p => captureMap[p], StringComparer.Ordinal);

        var annotated = ctor
            .Parameters.Select(p =>
                p.CapturedFieldName is null
                && byParamName.TryGetValue(p.Parameter.Name, out var fieldName)
                    ? p with
                    {
                        CapturedFieldName = fieldName,
                    }
                    : p
            )
            .ToList();

        return ctor with
        {
            Parameters = annotated,
        };
    }

    /// <summary>
    /// Walks the type's IR fields looking for a <c>= identifier</c>
    /// initializer that names one of the constructor's parameters; when
    /// found, annotates both halves: the parameter gets the field's name on
    /// <see cref="IrConstructorParameter.CapturedFieldName"/>, and the field
    /// gets <see cref="IrFieldDeclaration.IsCapturedByCtor"/> set to
    /// <c>true</c>. Backends use the pair to emit a
    /// <c>this._foo = foo</c> assignment in the constructor body and to
    /// suppress the field's initializer at the same time so the value is
    /// assigned exactly once.
    /// </summary>
    private static IrConstructorDeclaration? AnnotateCapturedParams(
        IrConstructorDeclaration? ctor,
        List<IrMemberDeclaration> members
    )
    {
        if (ctor is null || ctor.Parameters.Count == 0)
            return ctor;

        // paramName → fieldName + index of the capturing field in `members`,
        // scanning every field whose initializer is a bare identifier that
        // names a ctor parameter.
        Dictionary<string, (string FieldName, int FieldIndex)>? captures = null;
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is not IrFieldDeclaration { Initializer: IrIdentifier id } field)
                continue;
            var match = ctor.Parameters.FirstOrDefault(p =>
                string.Equals(p.Parameter.Name, id.Name, StringComparison.OrdinalIgnoreCase)
            );
            if (match is null)
                continue;
            captures ??= new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
            captures[match.Parameter.Name] = (field.Name, i);
        }
        if (captures is null)
            return ctor;

        // Mutate the members list in place so callers downstream see the
        // IsCapturedByCtor flag without us having to thread a new list back
        // to the parent extractor (the list is its own scratch buffer).
        foreach (var (fieldName, fieldIndex) in captures.Values)
            members[fieldIndex] = ((IrFieldDeclaration)members[fieldIndex]) with
            {
                IsCapturedByCtor = true,
            };

        var annotated = ctor
            .Parameters.Select(p =>
                captures.TryGetValue(p.Parameter.Name, out var capture)
                    ? p with
                    {
                        CapturedFieldName = capture.FieldName,
                    }
                    : p
            )
            .ToList();
        return ctor with { Parameters = annotated };
    }

    private static IrTypeSemantics ExtractSemantics(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver
    ) =>
        new(
            IsRecord: type.IsRecord,
            IsValueType: type.IsValueType,
            IsStatic: type.IsStatic,
            IsAbstract: type.IsAbstract && !type.IsStatic,
            IsSealed: type.IsSealed && !type.IsStatic && !type.IsValueType,
            IsPlainObject: SymbolHelper.HasPlainObject(type),
            IsException: IsException(type),
            IsBranded: SymbolHelper.HasBranded(type),
            BrandedUnderlyingType: ExtractBrandedUnderlyingType(type, originResolver),
            IsJsTuple: SymbolHelper.HasJsTuple(type),
            IsJsxComponent: SymbolHelper.IsJsxComponent(type),
            JsxNativeElementTag: SymbolHelper.GetJsxNativeElementTag(type),
            RendersAsJsxElement: SymbolHelper.IsJsxRenderable(type)
        );

    /// <summary>
    /// Extracts the underlying primitive of an <c>[Branded]</c> struct: the
    /// type of the single wrapped value member resolved by
    /// <see cref="SymbolHelper.GetBrandedValueType"/>. Returns <c>null</c>
    /// when the type isn't a real wrapper, signalling to the consumer that it
    /// should fall through to the regular class/record path.
    /// </summary>
    private static IrTypeRef? ExtractBrandedUnderlyingType(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver
    ) =>
        SymbolHelper.GetBrandedValueType(type) is { } valueType
            ? IrTypeRefMapper.Map(valueType, originResolver)
            : null;

    private static bool IsException(INamedTypeSymbol type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool IsMeaningfulBase(INamedTypeSymbol baseType)
    {
        if (baseType.SpecialType != SpecialType.None)
            return false;
        var name = baseType.ToDisplayString();
        return name != "System.Object" && name != "System.ValueType";
    }

    private static IrTypeParameter ExtractTypeParameter(
        ITypeParameterSymbol tp,
        IrTypeOriginResolver? originResolver
    ) =>
        new(
            tp.Name,
            tp.ConstraintTypes.Length > 0
                ? tp.ConstraintTypes.Select(t => IrTypeRefMapper.Map(t, originResolver)).ToList()
                : null
        );

    private static List<IrMemberDeclaration> ExtractMembers(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver,
        Compilation? compilation,
        Metano.Annotations.TargetLanguage? target
    )
    {
        // Collect methods first, grouped by name, so we can fold sibling
        // overloads onto a single primary IrMethodDeclaration. Backends that
        // don't support overloading (Dart) rely on the Overloads slot to drive
        // a diagnostic; if each sibling shows up as a separate member the
        // warning never fires and the output emits duplicate methods.
        var result = new List<IrMemberDeclaration>();
        var methodGroups = new Dictionary<string, List<IMethodSymbol>>(StringComparer.Ordinal);

        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            if (SymbolHelper.HasIgnore(member, target))
                continue;

            if (IsDroppedExplicitImplementation(member, target))
                continue;

            switch (member)
            {
                case IFieldSymbol field
                    when TryExtractField(field, originResolver, compilation, target) is { } f:
                    result.Add(f);
                    break;

                case IPropertySymbol prop:
                    var property = IrPropertyExtractor.Extract(
                        prop,
                        originResolver,
                        compilation,
                        target
                    );

                    result.Add(DeclareUnderInterfaceName(property, prop));
                    break;

                case IMethodSymbol method when IsUserMethod(method):
                    if (SymbolHelper.IsInlinedAtCallSite(method))
                        break;

                    var groupKey = OverloadGroupKey(method);
                    if (!methodGroups.TryGetValue(groupKey, out var list))
                    {
                        list = new List<IMethodSymbol>();
                        methodGroups[groupKey] = list;
                    }
                    list.Add(method);
                    break;

                case IEventSymbol evt:
                    result.Add(DeclareUnderInterfaceName(ExtractEvent(evt, originResolver), evt));
                    break;
            }
        }

        foreach (var group in methodGroups.Values)
        {
            // Pick the widest overload as the primary to match the dispatcher
            // convention (most specific arity first); siblings become the
            // Overloads payload.
            var ordered = group.OrderByDescending(m => m.Parameters.Length).ToList();
            var primary = ExtractMethod(ordered[0], originResolver, compilation, target);

            if (ordered.Count == 1)
            {
                result.Add(primary);
                continue;
            }

            var siblings = ordered
                .Skip(1)
                .Select(m => ExtractMethod(m, originResolver, compilation, target))
                .ToList();
            result.Add(primary with { Overloads = siblings });
        }

        return result;
    }

    private static bool IsUserMethod(IMethodSymbol method) =>
        method.MethodKind
            is MethodKind.Ordinary
                or MethodKind.UserDefinedOperator
                or MethodKind.Conversion
                or MethodKind.ExplicitInterfaceImplementation;

    /// <summary>
    /// Overloads share a group by C# name; an explicit implementation groups under
    /// its interface member's name, the name it is declared with.
    /// </summary>
    private static string OverloadGroupKey(IMethodSymbol method)
    {
        var name =
            ExplicitImplementationLowering.ImplementedInterfaceMember(method)?.Name ?? method.Name;

        return method.IsOverride ? name + "$override" : name;
    }

    /// <summary>
    /// An explicit interface implementation the target cannot declare: its interface
    /// is erased, it implements an operator, or its interface member name is already
    /// taken (the frontend reports <c>MS0031</c> for the latter unless the interface is
    /// outside the transpiled contract, such as a BCL bridge). See
    /// <see cref="ExplicitImplementationLowering"/>.
    /// </summary>
    private static bool IsDroppedExplicitImplementation(
        ISymbol member,
        Metano.Annotations.TargetLanguage? target
    ) =>
        ExplicitImplementationLowering.Resolve(member, target)
            is { Fate: not ExplicitImplementationFate.EmittedUnderInterfaceName };

    private static IrEventDeclaration ExtractEvent(
        IEventSymbol evt,
        IrTypeOriginResolver? originResolver
    ) =>
        new(
            evt.Name,
            IrVisibilityMapper.Map(evt.DeclaredAccessibility),
            evt.IsStatic,
            IrTypeRefMapper.Map(evt.Type, originResolver)
        )
        {
            Attributes = IrAttributeExtractor.Extract(evt),
        };

    private static IrMethodDeclaration ExtractMethod(
        IMethodSymbol method,
        IrTypeOriginResolver? originResolver,
        Compilation? compilation,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var declaration = IrMethodExtractor.Extract(method, originResolver, compilation, target);

        return DeclareUnderInterfaceName(declaration, method);
    }

    /// <summary>
    /// The targets have no explicit interface implementations, so an explicit member
    /// (<c>string IPlain.Name</c>) is declared as a public member under the interface
    /// member's name and attributes — exactly what every call site through the
    /// interface type emits. The interface member's attributes replace the explicit
    /// member's on purpose: call sites bind to the interface member, so its
    /// <c>[Name]</c> and <c>[ObjectArgs]</c> decide the declared shape. Any other
    /// member is returned unchanged.
    /// </summary>
    private static T DeclareUnderInterfaceName<T>(T declaration, ISymbol member)
        where T : IrMemberDeclaration
    {
        if (
            ExplicitImplementationLowering.ImplementedInterfaceMember(member) is not { } implemented
        )
            return declaration;

        IrMemberDeclaration renamed = declaration with
        {
            Name = implemented.Name,
            Visibility = IrVisibility.Public,
            Attributes = IrAttributeExtractor.Extract(implemented),
        };

        return (T)renamed;
    }

    private static IrFieldDeclaration? TryExtractField(
        IFieldSymbol field,
        IrTypeOriginResolver? originResolver,
        Compilation? compilation,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (field.AssociatedSymbol is IPropertySymbol)
            return null;
        if (field.ContainingType.TypeKind == TypeKind.Enum)
            return null;

        var initializer = compilation is not null
            ? TryExtractFieldInitializer(field, compilation, originResolver, target)
            : null;

        return new IrFieldDeclaration(
            field.Name,
            IrVisibilityMapper.Map(field.DeclaredAccessibility),
            field.IsStatic,
            IrTypeRefMapper.Map(field.Type, originResolver, target),
            IsReadonly: field.IsReadOnly,
            Initializer: initializer,
            IsConstant: field.HasConstant()
        )
        {
            Attributes = IrAttributeExtractor.Extract(field),
        };
    }

    private static IrExpression? TryExtractFieldInitializer(
        IFieldSymbol field,
        Compilation compilation,
        IrTypeOriginResolver? originResolver,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var declarator =
            field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax;
        if (declarator?.Initializer is null)
            return null;

        var model = compilation.GetSemanticModel(declarator.SyntaxTree);
        var expr = new IrExpressionExtractor(model, originResolver, target);
        return expr.ExtractStandalone(declarator.Initializer.Value);
    }
}
