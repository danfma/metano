using MetaSharp.Compiler;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Transforms an ordinary C# record / class / struct into a TypeScript class declaration.
/// This is the catch-all per-shape transformer — anything that isn't an enum, interface,
/// exception, inline-wrapper, or static module ends up here.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Resolves the base class (when transpilable) and the inherited primary-constructor params</item>
///   <item>Builds the TS constructor (single ctor inline; multiple ctors → <see cref="OverloadDispatcherBuilder"/>)</item>
///   <item>Detects captured primary-ctor params (DI-style) and emits <c>this._field = param</c> assignments</item>
///   <item>Walks fields, properties (auto/computed/getter+setter), ordinary methods (with overloads), and user-defined operators</item>
///   <item>For records: appends <c>equals</c> / <c>hashCode</c> / <c>with</c> via <see cref="RecordSynthesizer"/></item>
///   <item>Collects implemented (transpilable) interfaces and type parameters</item>
/// </list>
/// </summary>
public sealed class RecordClassTransformer(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    public void Transform(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        // [PlainObject] short-circuit: emit as a TS interface (data shape, no class
        // wrapper). The lowering of `new T(...)` and `with` is handled in
        // ObjectCreationHandler — both produce object literals instead of constructor
        // calls / `.with()` invocations, so the runtime form is plain JS data.
        if (SymbolHelper.HasPlainObject(type))
        {
            EmitAsPlainObject(type, statements);
            return;
        }

        // Resolve base class (if transpilable)
        TsType? extendsType = null;
        var baseParams = Array.Empty<TsConstructorParam>();

        if (type.BaseType is not null
            && type.BaseType.SpecialType == SpecialType.None
            && type.BaseType.ToDisplayString() != "System.Object"
            && type.BaseType.ToDisplayString() != "System.ValueType"
            && SymbolHelper.IsTranspilable(type.BaseType.OriginalDefinition, _context.AssemblyWideTranspile, _context.CurrentAssembly))
        {
            extendsType = TypeMapper.Map(type.BaseType);
            baseParams = GetConstructorParams(type.BaseType.OriginalDefinition).ToArray();
        }

        var ownParams = GetOwnConstructorParams(type);
        // All params for equals/hashCode/with (conceptual fields — both inherited and own)
        var allParams = baseParams.Concat(ownParams).ToList();
        // Constructor signature: only own params (base properties are declared in parent)
        var ctorParamsForSignature = ownParams.ToList();

        // Detect captured primary constructor params (used in field initializers but not properties)
        var capturedParams = GetCapturedConstructorParams(type, ctorParamsForSignature);
        ctorParamsForSignature.AddRange(capturedParams);
        var capturedParamNames = capturedParams.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Detect multiple constructors
        var explicitCtors = type.Constructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length > 0)
            .ToList();

        TsConstructor constructor;

        if (explicitCtors.Count > 1)
        {
            constructor = new OverloadDispatcherBuilder(_context)
                .BuildConstructor(type, explicitCtors, extendsType);
        }
        else
        {
            // Single constructor (original behavior)
            var ctorBody = new List<TsStatement>();
            if (extendsType is not null)
            {
                var superArgs = ResolveSuperArguments(type, baseParams);
                if (superArgs.Count > 0)
                {
                    ctorBody.Add(new TsExpressionStatement(
                        new TsCallExpression(new TsIdentifier("super"), superArgs)
                    ));
                }
            }

            // Add captured param assignments: this._field = param
            foreach (var captured in capturedParams)
            {
                var fieldName = GetCapturedFieldName(type, captured.Name);
                if (fieldName is not null)
                {
                    ctorBody.Add(new TsExpressionStatement(
                        new TsBinaryExpression(
                            new TsPropertyAccess(new TsIdentifier("this"), fieldName),
                            "=",
                            new TsIdentifier(captured.Name))));
                }
            }

            constructor = new TsConstructor(ctorParamsForSignature, ctorBody);
        }

        var classMembers = new List<TsClassMember>();

        // Fields, properties, operators
        var ordinaryMethods = new List<IMethodSymbol>();

        foreach (var member in type.GetMembers())
        {
            if (SymbolHelper.HasIgnore(member))
                continue;

            switch (member)
            {
                case IFieldSymbol field:
                    var fieldMember = TransformField(field, capturedParamNames);
                    if (fieldMember is not null)
                        classMembers.Add(fieldMember);
                    break;

                case IPropertySymbol prop when !IsConstructorParam(prop, ctorParamsForSignature):
                    var propMembers = TransformProperty(prop);
                    classMembers.AddRange(propMembers);
                    break;

                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary
                    && !method.IsImplicitlyDeclared
                    && method.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.NotApplicable)
                    && !TypeScriptNaming.HasEmit(method)
                    && method.AssociatedSymbol is not IPropertySymbol:
                    ordinaryMethods.Add(method);
                    break;

                case IMethodSymbol method when method.MethodKind == MethodKind.UserDefinedOperator:
                    classMembers.AddRange(TransformClassOperator(type, method));
                    break;
            }
        }

        // Process ordinary methods — detect overloads (same name, different signatures)
        var methodGroups = ordinaryMethods
            .GroupBy(m => m.Name)
            .ToList();

        foreach (var group in methodGroups)
        {
            var methods = group.ToList();
            if (methods.Count == 1)
            {
                // Single method — no dispatcher needed
                var classMember = TransformClassMethod(methods[0]);
                if (classMember is not null)
                    classMembers.Add(classMember);
            }
            else
            {
                // Multiple overloads — generate dispatcher
                var overloadMembers = new OverloadDispatcherBuilder(_context)
                    .BuildMethod(type, methods);
                classMembers.AddRange(overloadMembers);
            }
        }

        // Generate equals, hashCode, with for records (using ALL params including inherited)
        if (type.IsRecord)
        {
            classMembers.Add(RecordSynthesizer.GenerateEquals(type, allParams));
            classMembers.Add(RecordSynthesizer.GenerateHashCode(allParams));
            classMembers.Add(RecordSynthesizer.GenerateWith(type, allParams));
        }

        var implementsList = GetImplementedInterfaces(type);
        var typeParams = TypeTransformer.ExtractTypeParameters(type);

        statements.Add(new TsClass(
            type.Name,
            constructor,
            classMembers,
            Extends: extendsType,
            Implements: implementsList.Count > 0 ? implementsList : null,
            TypeParameters: typeParams
        ));
    }

    // ─── Constructor parameter discovery ──────────────────────

    private IReadOnlyList<TsConstructorParam> GetConstructorParams(INamedTypeSymbol type)
    {
        var primaryCtorParamDefaults = GetPrimaryConstructorParamDefaults(type);
        var ctorParams = new List<TsConstructorParam>();

        foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsImplicitlyDeclared) continue;
            if (member.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) continue;
            if (SymbolHelper.HasIgnore(member)) continue;
            if (!primaryCtorParamDefaults.ContainsKey(member.Name)) continue;

            var name = SymbolHelper.GetNameOverride(member) ?? TypeScriptNaming.ToCamelCase(member.Name);
            var tsType = TypeMapper.Map(member.Type);
            var isReadonly = member.SetMethod is null || member.SetMethod.IsInitOnly;
            var accessibility = TypeTransformer.MapAccessibility(member.DeclaredAccessibility);
            var defaultValue = primaryCtorParamDefaults[member.Name];

            ctorParams.Add(new TsConstructorParam(name, tsType, isReadonly, accessibility, defaultValue));
        }

        return ctorParams;
    }

    /// <summary>
    /// Returns only properties declared directly on this type (not inherited).
    /// </summary>
    private IReadOnlyList<TsConstructorParam> GetOwnConstructorParams(INamedTypeSymbol type)
    {
        var primaryCtorParamDefaults = GetPrimaryConstructorParamDefaults(type);
        var ctorParams = new List<TsConstructorParam>();

        foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsImplicitlyDeclared) continue;
            if (member.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) continue;
            if (SymbolHelper.HasIgnore(member)) continue;
            if (member.IsOverride) continue;
            if (!primaryCtorParamDefaults.ContainsKey(member.Name)) continue;

            var name = SymbolHelper.GetNameOverride(member) ?? TypeScriptNaming.ToCamelCase(member.Name);
            var tsType = TypeMapper.Map(member.Type);
            var isReadonly = member.SetMethod is null || member.SetMethod.IsInitOnly;
            var accessibility = TypeTransformer.MapAccessibility(member.DeclaredAccessibility);
            var defaultValue = primaryCtorParamDefaults[member.Name];

            ctorParams.Add(new TsConstructorParam(name, tsType, isReadonly, accessibility, defaultValue));
        }

        return ctorParams;
    }

    /// <summary>
    /// Gets the primary constructor parameter names and their default values (if any).
    /// Includes all constructors — for C# 12+ primary constructor classes,
    /// the constructor is IsImplicitlyDeclared (unlike records where it's explicit).
    /// </summary>
    private static Dictionary<string, TsExpression?> GetPrimaryConstructorParamDefaults(INamedTypeSymbol type)
    {
        var result = new Dictionary<string, TsExpression?>(StringComparer.OrdinalIgnoreCase);
        var primaryCtor = type.Constructors
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (primaryCtor is null) return result;

        foreach (var p in primaryCtor.Parameters)
        {
            TsExpression? defaultValue = null;
            if (p.HasExplicitDefaultValue)
            {
                // Check if the parameter type is a StringEnum — resolve to string literal
                if (p.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                    && SymbolHelper.HasStringEnum(enumType)
                    && p.ExplicitDefaultValue is int enumOrdinal)
                {
                    var enumMember = enumType.GetMembers().OfType<IFieldSymbol>()
                        .Where(f => f.HasConstantValue)
                        .FirstOrDefault(f => (int)f.ConstantValue! == enumOrdinal);

                    if (enumMember is not null)
                    {
                        var memberName = SymbolHelper.GetNameOverride(enumMember) ?? enumMember.Name;
                        defaultValue = new TsStringLiteral(memberName);
                    }
                    else
                    {
                        defaultValue = new TsLiteral(enumOrdinal.ToString());
                    }
                }
                else
                {
                    defaultValue = p.ExplicitDefaultValue switch
                    {
                        null => new TsLiteral("null"),
                        string s => new TsStringLiteral(s),
                        bool b => new TsLiteral(b ? "true" : "false"),
                        int i => new TsLiteral(i.ToString()),
                        long l => new TsLiteral(l.ToString()),
                        double d => new TsLiteral(d.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        _ => new TsLiteral(p.ExplicitDefaultValue.ToString()!)
                    };
                }
            }

            result[p.Name] = defaultValue;
        }

        return result;
    }

    // ─── Field / Property / Method transformation ─────────────

    /// <summary>
    /// Returns the TypeScript expression representing the default value for a C# type
    /// when no explicit initializer is provided. Mirrors C#'s <c>default(T)</c>
    /// semantics for value types and enums so the generated TS doesn't end up with
    /// <c>undefined</c> at runtime where C# would have a deterministic zero / false /
    /// first-enum-member default. Returns null for types where no automatic default
    /// makes sense (string, reference types, generic type parameters).
    /// </summary>
    private static TsExpression? ComputeDefaultInitializer(ITypeSymbol type)
    {
        // Nullable value types (int?, MyEnum?) → null
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            return new TsLiteral("null");

        // Nullable reference types → null
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return new TsLiteral("null");

        // Enums → first member (matches default(E) which is (E)0). Works for both
        // numeric and string enums because both lower to TS forms where
        // `EnumName.Member` resolves correctly.
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            var firstMember = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.IsConst)
                .OrderBy(f => f.ConstantValue switch
                {
                    int i => (long)i,
                    long l => l,
                    _ => long.MaxValue,
                })
                .FirstOrDefault();
            if (firstMember is null) return null;
            var enumName = TypeTransformer.GetTsTypeName(enumType);
            return new TsPropertyAccess(new TsIdentifier(enumName), firstMember.Name);
        }

        // Numeric primitives → 0
        if (type.SpecialType is SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64
            or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64
            or SpecialType.System_Byte or SpecialType.System_SByte
            or SpecialType.System_Single or SpecialType.System_Double)
        {
            return new TsLiteral("0");
        }

        // decimal → new Decimal("0"). Matches the literal handling form so the
        // emitted Decimal references are syntactically consistent across the file.
        if (type.SpecialType == SpecialType.System_Decimal)
        {
            return new TsNewExpression(
                new TsIdentifier("Decimal"),
                [new TsStringLiteral("0")]);
        }

        // bool → false
        if (type.SpecialType == SpecialType.System_Boolean)
            return new TsLiteral("false");

        // string, reference types, type parameters: no automatic default — the C#
        // compiler would either default these to null (with nullable annotations) or
        // require an explicit initializer.
        return null;
    }

    private TsFieldMember? TransformField(IFieldSymbol field, HashSet<string>? capturedParamNames)
    {
        if (field.IsImplicitlyDeclared) return null;
        if (field.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) return null;
        if (SymbolHelper.HasIgnore(field)) return null;
        // Skip backing fields for auto-properties (compiler-generated)
        if (field.AssociatedSymbol is not null) return null;

        var name = SymbolHelper.GetNameOverride(field) ?? TypeScriptNaming.ToCamelCase(field.Name);
        var tsType = TypeMapper.Map(field.Type);
        var isReadonly = field.IsReadOnly;
        var accessibility = TypeTransformer.MapAccessibility(field.DeclaredAccessibility);

        // Try to get initializer from syntax
        TsExpression? initializer = null;
        var syntax = field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is VariableDeclaratorSyntax { Initializer: not null } varDecl)
        {
            // Skip initializer if it references a captured constructor param
            // (the assignment is moved to the constructor body)
            var isCapuredInit = varDecl.Initializer.Value is IdentifierNameSyntax initId
                && capturedParamNames is not null
                && capturedParamNames.Contains(TypeScriptNaming.ToCamelCase(initId.Identifier.Text));

            if (!isCapuredInit)
            {
                var semanticModel = _context.Compilation.GetSemanticModel(varDecl.SyntaxTree);
                var exprTransformer = _context.CreateExpressionTransformer(semanticModel);
                initializer = exprTransformer.TransformExpression(varDecl.Initializer.Value);
            }
        }

        // No explicit initializer → fall back to default(T) for value types so the
        // runtime behavior matches C# (enum → first member, int → 0, bool → false,
        // decimal → new Decimal("0"), etc.). Reference types and string stay null.
        initializer ??= ComputeDefaultInitializer(field.Type);

        return new TsFieldMember(name, tsType, initializer, isReadonly, accessibility);
    }

    private static bool IsConstructorParam(IPropertySymbol prop, IReadOnlyList<TsConstructorParam> ctorParams)
    {
        var name = SymbolHelper.GetNameOverride(prop) ?? TypeScriptNaming.ToCamelCase(prop.Name);
        return ctorParams.Any(p => p.Name == name);
    }

    /// <summary>
    /// Transforms a non-constructor property into getter/setter/field AST members.
    /// </summary>
    private IReadOnlyList<TsClassMember> TransformProperty(IPropertySymbol prop)
    {
        if (prop.IsImplicitlyDeclared) return [];
        if (prop.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) return [];
        if (SymbolHelper.HasIgnore(prop)) return [];

        var name = SymbolHelper.GetNameOverride(prop) ?? TypeScriptNaming.ToCamelCase(prop.Name);
        var tsType = TypeMapper.Map(prop.Type);
        var accessibility = TypeTransformer.MapAccessibility(prop.DeclaredAccessibility);
        var results = new List<TsClassMember>();

        // Check if the property has explicit accessor bodies
        var syntax = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as PropertyDeclarationSyntax;

        var hasGetterBody = syntax?.ExpressionBody is not null
            || syntax?.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration) && (a.Body is not null || a.ExpressionBody is not null)) == true;
        var hasSetterBody = syntax?.AccessorList?.Accessors.Any(a =>
            a.IsKind(SyntaxKind.SetAccessorDeclaration) && (a.Body is not null || a.ExpressionBody is not null)) == true;

        if (hasGetterBody || syntax?.ExpressionBody is not null)
        {
            // Computed property → getter
            var semanticModel = _context.Compilation.GetSemanticModel(syntax!.SyntaxTree);
            var exprTransformer = _context.CreateExpressionTransformer(semanticModel);
            exprTransformer.SelfParameterName = "this";

            IReadOnlyList<TsStatement> getterBody;
            if (syntax.ExpressionBody is not null)
            {
                getterBody = [new TsReturnStatement(exprTransformer.TransformExpression(syntax.ExpressionBody.Expression))];
            }
            else
            {
                var getAccessor = syntax.AccessorList!.Accessors.First(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                getterBody = exprTransformer.TransformBody(getAccessor.Body, getAccessor.ExpressionBody);
            }

            results.Add(new TsGetterMember(name, tsType, getterBody));
        }

        if (hasSetterBody)
        {
            var setAccessor = syntax!.AccessorList!.Accessors.First(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
            var semanticModel = _context.Compilation.GetSemanticModel(syntax.SyntaxTree);
            var exprTransformer = _context.CreateExpressionTransformer(semanticModel);
            exprTransformer.SelfParameterName = "this";
            var setterBody = exprTransformer.TransformBody(setAccessor.Body, setAccessor.ExpressionBody);
            var valueParam = new TsParameter("value", tsType);

            results.Add(new TsSetterMember(name, valueParam, setterBody));
        }

        // Auto-property (no custom bodies) that isn't a ctor param → field
        if (!hasGetterBody && syntax?.ExpressionBody is null)
        {
            var isReadonly = prop.SetMethod is null || prop.SetMethod.IsInitOnly;
            TsExpression? initializer = null;

            if (syntax?.Initializer is not null)
            {
                var semanticModel = _context.Compilation.GetSemanticModel(syntax.SyntaxTree);
                var exprTransformer = _context.CreateExpressionTransformer(semanticModel);
                exprTransformer.SelfParameterName = "this";
                initializer = exprTransformer.TransformExpression(syntax.Initializer.Value);
            }
            else
            {
                // No explicit `= ...` initializer → fall back to default(T). Handles
                // both nullable types (→ null, via the nullable branch inside the
                // helper) and value types (enum → first member, int → 0, etc.) so
                // the runtime behavior matches C#.
                initializer = ComputeDefaultInitializer(prop.Type);
            }

            results.Add(new TsFieldMember(name, tsType, initializer, isReadonly, accessibility));
        }

        return results;
    }

    private TsClassMember? TransformClassMethod(IMethodSymbol method)
    {
        if (method.IsImplicitlyDeclared) return null;
        // [Emit] methods are consumed inline at call sites, not generated as methods
        if (TypeScriptNaming.HasEmit(method)) return null;
        // Skip compiler-generated or internal/unsupported access levels
        if (method.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) return null;

        var syntax =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as MethodDeclarationSyntax;
        if (syntax is null) return null;

        // Method declarations use the member-name camelCase (no reserved-word
        // escape) so a method named `Delete` becomes `delete()` instead of
        // `delete_()`. Both the declaration and the call site (MemberAccessHandler)
        // route through the same non-escaping helper, so they stay in agreement.
        var name = SymbolHelper.GetNameOverride(method) ?? TypeScriptNaming.ToCamelCaseMember(method.Name);
        var hasYield = syntax.DescendantNodes().OfType<YieldStatementSyntax>().Any();
        var returnType = hasYield
            ? TypeMapper.MapForGeneratorReturn(method.ReturnType)
            : TypeMapper.Map(method.ReturnType);
        var isAsync = hasYield ? false : method.IsAsync;

        var parameters = method.Parameters
            .Select(p => new TsParameter(TypeScriptNaming.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
            .ToList();

        var semanticModel = _context.Compilation.GetSemanticModel(syntax.SyntaxTree);
        var exprTransformer = _context.CreateExpressionTransformer(semanticModel);

        // Instance methods use 'this' — set self parameter name to "this"
        if (!method.IsStatic)
            exprTransformer.SelfParameterName = "this";

        var body = exprTransformer.TransformBody(syntax.Body, syntax.ExpressionBody, isVoid: method.ReturnsVoid);

        return new TsMethodMember(
            name,
            parameters,
            returnType,
            body,
            Static: method.IsStatic,
            Async: isAsync,
            Generator: hasYield,
            Accessibility: TypeTransformer.MapAccessibility(method.DeclaredAccessibility),
            TypeParameters: TypeTransformer.ExtractMethodTypeParameters(method)
        );
    }

    private IReadOnlyList<TsClassMember> TransformClassOperator(
        INamedTypeSymbol containingType,
        IMethodSymbol method)
    {
        var nameOverride = SymbolHelper.GetNameOverride(method);
        if (nameOverride is null) return [];

        var syntax =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as OperatorDeclarationSyntax;
        if (syntax is null) return [];

        var returnType = TypeMapper.Map(method.ReturnType);

        var parameters = method.Parameters
            .Select(p => new TsParameter(TypeScriptNaming.ToCamelCase(p.Name), TypeMapper.Map(p.Type)))
            .ToList();

        var semanticModel = _context.Compilation.GetSemanticModel(syntax.SyntaxTree);
        var exprTransformer = _context.CreateExpressionTransformer(semanticModel);
        var body = exprTransformer.TransformBody(syntax.Body, syntax.ExpressionBody);

        var staticName = $"__{nameOverride}";
        var isUnary = method.Parameters.Length == 1;
        var results = new List<TsClassMember>();

        // Static operator method: static __add(left, right) or static __negate(operand)
        results.Add(new TsMethodMember(staticName, parameters, returnType, body, Static: true));

        // Instance helper: $add(right) or $negate()
        if (isUnary)
        {
            // Unary: $negate(): Type { return ClassName.__negate(this); }
            var helperBody = new TsReturnStatement(
                new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier(containingType.Name), staticName),
                    [new TsIdentifier("this")]
                )
            );
            results.Add(new TsMethodMember($"${nameOverride}", [], returnType, [helperBody]));
        }
        else
        {
            // Binary: $add(right): Type { return ClassName.__add(this, right); }
            var rightParam = parameters.Last();
            var helperBody = new TsReturnStatement(
                new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier(containingType.Name), staticName),
                    [new TsIdentifier("this"), new TsIdentifier(rightParam.Name)]
                )
            );
            results.Add(new TsMethodMember($"${nameOverride}", [rightParam], returnType, [helperBody]));
        }

        return results;
    }

    // ─── Captured primary-ctor params (DI-style) ──────────────

    /// <summary>
    /// Detects primary constructor parameters that are captured in field initializers
    /// but are not properties (e.g., DI params like <c>IssueService(IIssueRepository repository)</c>).
    /// These need to be added to the TS constructor signature.
    /// </summary>
    private static IReadOnlyList<TsConstructorParam> GetCapturedConstructorParams(
        INamedTypeSymbol type,
        IReadOnlyList<TsConstructorParam> existingParams)
    {
        var primaryCtor = type.Constructors
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
        if (primaryCtor is null) return [];

        var existingNames = existingParams.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<TsConstructorParam>();

        foreach (var param in primaryCtor.Parameters)
        {
            var camelName = TypeScriptNaming.ToCamelCase(param.Name);
            if (existingNames.Contains(camelName)) continue;

            // Check if this param is referenced by any field initializer
            var isCapured = type.GetMembers().OfType<IFieldSymbol>()
                .Any(f =>
                {
                    var syntax = f.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    if (syntax is VariableDeclaratorSyntax { Initializer.Value: IdentifierNameSyntax id })
                        return string.Equals(id.Identifier.Text, param.Name, StringComparison.OrdinalIgnoreCase);
                    return false;
                });

            if (isCapured)
            {
                result.Add(new TsConstructorParam(
                    camelName,
                    TypeMapper.Map(param.Type),
                    Accessibility: TsAccessibility.None));
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the TS field name for a captured constructor param (e.g., "repository" → "_repository").
    /// </summary>
    private static string? GetCapturedFieldName(INamedTypeSymbol type, string paramName)
    {
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            var syntax = field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (syntax is VariableDeclaratorSyntax { Initializer.Value: IdentifierNameSyntax id }
                && string.Equals(id.Identifier.Text, paramName, StringComparison.OrdinalIgnoreCase))
            {
                return TypeScriptNaming.ToCamelCase(field.Name);
            }
        }
        return null;
    }

    // ─── Implemented interfaces + base ctor resolution ────────

    /// <summary>
    /// Collects transpilable interfaces implemented by a type, returning their TS names.
    /// </summary>
    private List<TsType> GetImplementedInterfaces(INamedTypeSymbol type)
    {
        var result = new List<TsType>();
        foreach (var iface in type.Interfaces)
        {
            if (SymbolHelper.IsTranspilable(iface.OriginalDefinition, _context.AssemblyWideTranspile, _context.CurrentAssembly))
            {
                var tsName = TypeTransformer.GetTsTypeName(iface.OriginalDefinition);
                if (iface.TypeArguments.Length > 0)
                {
                    var args = iface.TypeArguments.Select(TypeMapper.Map).ToList();
                    result.Add(new TsNamedType(tsName, args));
                }
                else
                {
                    result.Add(new TsNamedType(tsName));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Resolves the actual arguments passed to the base constructor.
    /// For <c>record Ok(T Value) : Result(Value, true)</c>, returns
    /// <c>[Identifier("value"), Literal("true")]</c>. Falls back to passing base param names
    /// if the syntax can't be resolved.
    /// </summary>
    private IReadOnlyList<TsExpression> ResolveSuperArguments(
        INamedTypeSymbol type,
        IReadOnlyList<TsConstructorParam> baseParams)
    {
        // Try to find PrimaryConstructorBaseTypeSyntax in the type's declaration
        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax typeDecl) continue;
            if (typeDecl.BaseList is null) continue;

            foreach (var baseType in typeDecl.BaseList.Types)
            {
                if (baseType is PrimaryConstructorBaseTypeSyntax primaryBase)
                {
                    var semanticModel = _context.Compilation.GetSemanticModel(primaryBase.SyntaxTree);
                    var exprTransformer = _context.CreateExpressionTransformer(semanticModel);
                    return primaryBase.ArgumentList.Arguments
                        .Select(a => exprTransformer.TransformExpression(a.Expression))
                        .ToList();
                }
            }
        }

        // Fallback: pass base param names
        return baseParams
            .Select<TsConstructorParam, TsExpression>(p => new TsIdentifier(p.Name))
            .ToList();
    }

    /// <summary>
    /// Emits a <c>[PlainObject]</c> type as a TypeScript <c>interface</c> rather than a
    /// class. The properties come from the record's primary constructor parameters
    /// (same source as the class form, just rendered as interface members instead of
    /// constructor params + readonly fields). Methods, equality, and <c>with</c>
    /// helpers are intentionally omitted — the user opted into "data only" semantics
    /// when they applied the attribute.
    ///
    /// <para>Constructor params with default values become OPTIONAL fields in the
    /// emitted interface (<c>name?: Type</c>). The <c>ObjectCreationHandler</c>
    /// already drops omitted args from the literal it emits at construction time, so
    /// the two halves agree: omitting an arg in C# produces a literal without the
    /// key, and the receiving side can read JSON that omits the field without TS
    /// type errors.</para>
    /// </summary>
    private void EmitAsPlainObject(INamedTypeSymbol type, List<TsTopLevel> statements)
    {
        var paramDefaults = GetPrimaryConstructorParamDefaults(type);
        var ownParams = GetOwnConstructorParams(type);

        // GetOwnConstructorParams uses TypeScript's camelCase + [Name] override for
        // the param name, but the defaults map is keyed off the original C# property
        // name. We need to recover the C# name to look up "is this default-valued?".
        // Walk the type's properties in lockstep with the param list.
        var orderedProps = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsImplicitlyDeclared
                        && p.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.NotApplicable)
                        && !SymbolHelper.HasIgnore(p)
                        && !p.IsOverride
                        && paramDefaults.ContainsKey(p.Name))
            .ToList();

        var properties = new List<TsProperty>(ownParams.Count);
        for (var i = 0; i < ownParams.Count && i < orderedProps.Count; i++)
        {
            var p = ownParams[i];
            var hasDefault = paramDefaults[orderedProps[i].Name] is not null;
            properties.Add(new TsProperty(p.Name, p.Type, Readonly: true, Optional: hasDefault));
        }

        var name = SymbolHelper.GetNameOverride(type) ?? type.Name;
        statements.Add(new TsInterface(name, properties));

        // Emit instance methods as standalone helper functions that take the type as
        // their first parameter (the explicit `self`). The method body is transformed
        // with `SelfParameterName = "self"` so any reference to `this` (or implicit
        // `this.member`) inside the body rewrites to `self`.
        EmitPlainObjectMethods(type, name, statements);
    }

    /// <summary>
    /// Walks the type's instance methods and emits one TS helper function per
    /// method. The first parameter is always <c>self: T</c>; the rest mirror the C#
    /// signature. Static methods, operator overloads, and compiler-generated
    /// members are skipped.
    /// </summary>
    private void EmitPlainObjectMethods(INamedTypeSymbol type, string typeName, List<TsTopLevel> statements)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.IsImplicitlyDeclared) continue;
            if (method.MethodKind != MethodKind.Ordinary) continue;
            if (method.IsStatic) continue;
            if (method.DeclaredAccessibility is Accessibility.Internal or Accessibility.NotApplicable) continue;
            if (SymbolHelper.HasIgnore(method)) continue;
            if (TypeScriptNaming.HasEmit(method)) continue;

            var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                as MethodDeclarationSyntax;
            if (syntax is null) continue;

            var semanticModel = _context.Compilation.GetSemanticModel(syntax.SyntaxTree);
            var exprTransformer = _context.CreateExpressionTransformer(semanticModel);
            // Inside a [PlainObject] method body, `this` rewrites to `self`. The
            // member-access transformer reads SelfParameterName off the expression
            // transformer when emitting bare identifiers (instance member references).
            exprTransformer.SelfParameterName = "self";

            var body = exprTransformer.TransformBody(
                syntax.Body, syntax.ExpressionBody, isVoid: method.ReturnsVoid);

            // First parameter is always `self: T`; the rest are the C# parameters.
            var selfParam = new TsParameter("self", new TsNamedType(typeName));
            var parameters = new List<TsParameter> { selfParam };
            parameters.AddRange(method.Parameters
                .Select(p => new TsParameter(TypeScriptNaming.ToCamelCase(p.Name), TypeMapper.Map(p.Type))));

            // Function name escapes reserved words because top-level function
            // declarations CANNOT use them (`function delete() {}` is illegal even
            // though `obj.delete` is fine). The InvocationHandler call-site rewrite
            // routes through the same ToCamelCase variant.
            var fnName = SymbolHelper.GetNameOverride(method) ?? TypeScriptNaming.ToCamelCase(method.Name);
            var returnType = TypeMapper.Map(method.ReturnType);
            statements.Add(new TsFunction(
                fnName,
                parameters,
                returnType,
                body,
                Exported: true,
                Async: method.IsAsync));
        }
    }
}
