using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Extracts constructor declarations from a type. The primary constructor (the one
/// whose parameters match type properties) becomes the main <see cref="IrConstructorDeclaration"/>;
/// additional explicit constructors are attached as overloads. Parameters matching a
/// public property of the type get the appropriate promotion flag.
/// <para>
/// Body extraction and default-value expressions are deferred to a later phase —
/// this extractor only captures the <em>shape</em> of constructors.
/// </para>
/// </summary>
public static class IrConstructorExtractor
{
    public static IrConstructorDeclaration? Extract(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver = null,
        Compilation? compilation = null,
        Metano.Annotations.TargetLanguage? target = null
    )
    {
        var explicitCtors = ExplicitConstructors(type);
        var primary = FindPrimaryConstructor(type);

        if (primary is null)
            return null;

        var primaryDecl = BuildFromMethod(primary, type, originResolver, compilation, target);

        // Other constructors become overloads (without nested Overloads of their own).
        var overloads = explicitCtors
            .Where(c => !SymbolEqualityComparer.Default.Equals(c, primary))
            .Select(c => BuildFromMethod(c, type, originResolver, compilation, target))
            .ToList();

        return overloads.Count > 0 ? primaryDecl with { Overloads = overloads } : primaryDecl;
    }

    /// <summary>
    /// The constructor whose parameters become the emitted primary
    /// constructor: the one with the most parameters — for records always the
    /// explicit positional ctor; for C# 12+ primary constructor classes the
    /// implicit one. Promotions are keyed off it, and any target building
    /// positional <c>new T(...)</c> calls must follow its parameter order.
    /// Returns <c>null</c> when the type only has the implicit parameterless
    /// constructor (or the record copy constructor).
    /// </summary>
    public static IMethodSymbol? FindPrimaryConstructor(INamedTypeSymbol type) =>
        ExplicitConstructors(type).OrderByDescending(c => c.Parameters.Length).FirstOrDefault();

    /// <summary>
    /// The public constructors among those <see cref="Extract"/> models — the
    /// ones a target exposes, which drive multi-constructor dispatch.
    /// </summary>
    public static List<IMethodSymbol> PublicExplicitConstructors(INamedTypeSymbol type) =>
        ExplicitConstructors(type)
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

    private static List<IMethodSymbol> ExplicitConstructors(INamedTypeSymbol type) =>
        type
            .Constructors.Where(c =>
                (!c.IsImplicitlyDeclared || c.Parameters.Length > 0) && !IsRecordCopyConstructor(c)
            )
            .ToList();

    private static bool IsRecordCopyConstructor(IMethodSymbol ctor) =>
        ctor.IsImplicitlyDeclared
        && ctor.ContainingType.IsRecord
        && ctor.Parameters.Length == 1
        && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, ctor.ContainingType);

    private static IrConstructorDeclaration BuildFromMethod(
        IMethodSymbol ctor,
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver,
        Compilation? compilation,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var propertiesByName = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                !p.IsImplicitlyDeclared
                && p.DeclaredAccessibility
                    is not (Accessibility.Internal or Accessibility.NotApplicable)
                && !SymbolHelper.HasIgnore(p, target)
                && !p.IsOverride
            )
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var parameters = ctor
            .Parameters.Select(p =>
            {
                var (promotion, promotedVisibility, emittedName) = ResolvePromotionInfo(
                    p,
                    propertiesByName,
                    target
                );
                return new IrConstructorParameter(
                    new IrParameter(
                        p.Name,
                        IrTypeRefMapper.Map(p.Type, originResolver),
                        p.HasExplicitDefaultValue,
                        ExtractDefaultValue(p, compilation, originResolver),
                        IsParams: p.IsParams,
                        IsOptional: p.HasOptional(),
                        IsConstant: p.HasConstant()
                    ),
                    promotion,
                    PromotedVisibility: promotedVisibility,
                    EmittedName: emittedName
                );
            })
            .ToList();

        var body = compilation is not null
            ? TryExtractBody(ctor, compilation, originResolver)
            : null;
        var baseArguments = compilation is not null
            ? TryExtractBaseArguments(ctor, compilation, originResolver)
            : null;

        return new IrConstructorDeclaration(
            Parameters: parameters,
            Body: body,
            BaseArguments: baseArguments
        );
    }

    private static IReadOnlyList<IrStatement>? TryExtractBody(
        IMethodSymbol ctor,
        Compilation compilation,
        IrTypeOriginResolver? originResolver
    )
    {
        var syntax =
            ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as ConstructorDeclarationSyntax;
        if (syntax is null || (syntax.Body is null && syntax.ExpressionBody is null))
            return null;

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var extractor = new IrStatementExtractor(model, originResolver);
        return extractor.ExtractBody(syntax.Body, syntax.ExpressionBody, isVoid: true);
    }

    /// <summary>
    /// Captures the arguments of a <c>: base(...)</c> initializer, if any. The
    /// constructor dispatcher bridge uses these to emit the matching
    /// <c>super(...)</c> call at the top of each branch. <c>: this(...)</c>
    /// initializers are chained constructor calls and have no runtime-visible
    /// <c>super</c> — we leave them null so the bridge skips the super emission.
    /// </summary>
    private static IReadOnlyList<IrArgument>? TryExtractBaseArguments(
        IMethodSymbol ctor,
        Compilation compilation,
        IrTypeOriginResolver? originResolver
    )
    {
        var declaringSyntax = ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        // Explicit constructor with `: base(...)` initializer.
        if (declaringSyntax is ConstructorDeclarationSyntax explicitCtor)
        {
            if (explicitCtor.Initializer is null)
                return null;
            if (explicitCtor.Initializer.ThisOrBaseKeyword.Text != "base")
                return null;
            return MapArgumentList(
                explicitCtor.Initializer.ArgumentList,
                explicitCtor.SyntaxTree,
                compilation,
                originResolver
            );
        }

        // C# 12 primary constructor with base call: the synthesized ctor's
        // declaring syntax is the type itself, and the base arguments live on
        // a `PrimaryConstructorBaseTypeSyntax` inside the type's BaseList.
        var primaryBase = declaringSyntax switch
        {
            ClassDeclarationSyntax cls => FindPrimaryBase(cls.BaseList),
            StructDeclarationSyntax st => FindPrimaryBase(st.BaseList),
            RecordDeclarationSyntax rec => FindPrimaryBase(rec.BaseList),
            _ => null,
        };
        return primaryBase is null
            ? null
            : MapArgumentList(
                primaryBase.ArgumentList,
                primaryBase.SyntaxTree,
                compilation,
                originResolver
            );
    }

    private static PrimaryConstructorBaseTypeSyntax? FindPrimaryBase(BaseListSyntax? baseList) =>
        baseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault();

    private static IReadOnlyList<IrArgument> MapArgumentList(
        ArgumentListSyntax argumentList,
        SyntaxTree tree,
        Compilation compilation,
        IrTypeOriginResolver? originResolver
    )
    {
        var model = compilation.GetSemanticModel(tree);
        var expr = new IrExpressionExtractor(model, originResolver);
        return argumentList
            .Arguments.Select(a => new IrArgument(
                expr.ExtractStandalone(a.Expression),
                a.NameColon?.Name.Identifier.ValueText
            ))
            .ToList();
    }

    /// <summary>
    /// Extracts the C# default-value expression of a parameter into IR when we have the
    /// syntax tree + semantic model available. Returns <c>null</c> for parameters
    /// without an explicit default, for parameters declared in a referenced assembly
    /// whose syntax isn't reachable, or when no compilation is supplied (the caller is
    /// then operating in a shape-only mode).
    /// </summary>
    private static IrExpression? ExtractDefaultValue(
        IParameterSymbol parameter,
        Compilation? compilation,
        IrTypeOriginResolver? originResolver
    )
    {
        if (!parameter.HasExplicitDefaultValue || compilation is null)
            return null;
        var syntax =
            parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ParameterSyntax;
        var defaultExpression = syntax?.Default?.Value;
        if (defaultExpression is null)
            return null;
        var model = compilation.GetSemanticModel(syntax!.SyntaxTree);
        return new IrExpressionExtractor(model, originResolver).Extract(defaultExpression);
    }

    /// <summary>
    /// Resolves the promotion mode for a primary-constructor parameter and,
    /// when promoted, surfaces the matching property's visibility and any
    /// target-scoped <c>[Name]</c> override so backends don't need to walk
    /// Roslyn again to render the shorthand declaration.
    /// </summary>
    private static (
        IrParameterPromotion Promotion,
        IrVisibility? Visibility,
        string? EmittedName
    ) ResolvePromotionInfo(
        IParameterSymbol param,
        Dictionary<string, IPropertySymbol> propertiesByName,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (!propertiesByName.TryGetValue(param.Name, out var prop))
            return (IrParameterPromotion.None, null, null);

        var promotion =
            prop.SetMethod is null || prop.SetMethod.IsInitOnly
                ? IrParameterPromotion.ReadonlyProperty
                : IrParameterPromotion.MutableProperty;
        return (
            promotion,
            IrVisibilityMapper.Map(prop.DeclaredAccessibility),
            SymbolHelper.GetNameOverride(prop, target)
        );
    }
}
