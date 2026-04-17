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
/// extraction is available. Nested types are <em>not yet</em> extracted.
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

        var interfaces =
            type.Interfaces.Length > 0
                ? type.Interfaces.Select(i => IrTypeRefMapper.Map(i, originResolver)).ToList()
                : null;

        var typeParameters =
            type.TypeParameters.Length > 0
                ? type
                    .TypeParameters.Select(tp => ExtractTypeParameter(tp, originResolver))
                    .ToList()
                : null;

        var members = ExtractMembers(type, originResolver, compilation, target);
        var constructor = IrConstructorExtractor.Extract(type, originResolver, compilation, target);

        // Mark constructor parameters that are captured by a field
        // initializer (DI-style: `private readonly IFoo _foo = foo;`). The
        // extractor needs both halves to be available — the field IR for the
        // identifier match, the constructor IR for the parameter slot — so
        // it runs as a post-processing pass after the regular member /
        // constructor extraction completes.
        constructor = AnnotateCapturedParams(constructor, members);

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
            Attributes: IrAttributeExtractor.Extract(type)
        );
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
            IsInlineWrapper: SymbolHelper.HasInlineWrapper(type),
            InlineWrappedType: ExtractInlineWrappedType(type, originResolver)
        );

    /// <summary>
    /// Extracts the underlying primitive of an <c>[InlineWrapper]</c> struct.
    /// The wrapper shape requires exactly one public non-static value member
    /// (a field or a getter-shaped property); anything else — multiple
    /// members, parameterized properties, ignored members — disqualifies the
    /// type and we return <c>null</c>, signalling to the consumer that the
    /// type isn't a real wrapper and should fall through to the regular
    /// class/record path.
    /// </summary>
    private static IrTypeRef? ExtractInlineWrappedType(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver
    )
    {
        if (!SymbolHelper.HasInlineWrapper(type))
            return null;

        ITypeSymbol? wrapped = null;
        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared || member.IsStatic)
                continue;
            if (member.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (SymbolHelper.HasIgnore(member))
                continue;

            ITypeSymbol? candidate = member switch
            {
                IFieldSymbol f => f.Type,
                IPropertySymbol p when p.GetMethod is not null && p.Parameters.Length == 0 =>
                    p.Type,
                _ => null,
            };
            if (candidate is null)
                continue;

            if (wrapped is not null)
                return null; // more than one value member → not a wrapper
            wrapped = candidate;
        }

        return wrapped is null ? null : IrTypeRefMapper.Map(wrapped, originResolver);
    }

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

            switch (member)
            {
                case IFieldSymbol field
                    when TryExtractField(field, originResolver, compilation, target) is { } f:
                    result.Add(f);
                    break;

                case IPropertySymbol prop:
                    result.Add(
                        IrPropertyExtractor.Extract(prop, originResolver, compilation, target)
                    );
                    break;

                case IMethodSymbol method when IsUserMethod(method):
                    // [Emit] methods are inline templates with no real body —
                    // they exist only to be substituted at every call site
                    // through the call-site extractor. They must not surface
                    // as class members; including them would emit a stub
                    // declaration whose generated body would never be called.
                    if (SymbolHelper.HasEmit(method))
                        break;
                    if (!methodGroups.TryGetValue(method.Name, out var list))
                    {
                        list = new List<IMethodSymbol>();
                        methodGroups[method.Name] = list;
                    }
                    list.Add(method);
                    break;

                case IEventSymbol evt:
                    result.Add(
                        new IrEventDeclaration(
                            evt.Name,
                            IrVisibilityMapper.Map(evt.DeclaredAccessibility),
                            evt.IsStatic,
                            IrTypeRefMapper.Map(evt.Type, originResolver)
                        )
                        {
                            Attributes = IrAttributeExtractor.Extract(evt),
                        }
                    );
                    break;
            }
        }

        foreach (var group in methodGroups.Values)
        {
            // Pick the widest overload as the primary to match the dispatcher
            // convention (most specific arity first); siblings become the
            // Overloads payload.
            var ordered = group.OrderByDescending(m => m.Parameters.Length).ToList();
            var primary = IrMethodExtractor.Extract(
                ordered[0],
                originResolver,
                compilation,
                target
            );
            if (ordered.Count == 1)
            {
                result.Add(primary);
                continue;
            }
            var siblings = ordered
                .Skip(1)
                .Select(m => IrMethodExtractor.Extract(m, originResolver, compilation, target))
                .ToList();
            result.Add(primary with { Overloads = siblings });
        }

        return result;
    }

    private static bool IsUserMethod(IMethodSymbol method) =>
        method.MethodKind
            is MethodKind.Ordinary
                or MethodKind.UserDefinedOperator
                or MethodKind.Conversion;

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
            Initializer: initializer
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
        return expr.Extract(declarator.Initializer.Value);
    }
}
