using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    ) =>
        Extract(
            type,
            originResolver,
            compilation,
            target,
            PrimaryConstructorPromotion.For(type, compilation, target)
        );

    /// <summary>
    /// <see cref="Extract(INamedTypeSymbol, IrTypeOriginResolver?, Compilation?, Metano.Annotations.TargetLanguage?)"/>
    /// with the type's primary-constructor <paramref name="promotion"/>
    /// already decided, so a caller that also scans captures shares it.
    /// </summary>
    internal static IrConstructorDeclaration? Extract(
        INamedTypeSymbol type,
        IrTypeOriginResolver? originResolver,
        Compilation? compilation,
        Metano.Annotations.TargetLanguage? target,
        PrimaryConstructorPromotion promotion
    )
    {
        var explicitCtors = ExplicitConstructors(type);
        var primary = FindPrimaryConstructor(type);

        if (primary is null)
            return ImplicitConstructorWithObservableBaseCall(type, target);

        var context = new PromotionContext(type, compilation, target, promotion);
        var primaryDecl = BuildFromMethod(primary, originResolver, context);

        // Other constructors become overloads (without nested Overloads of their own).
        var overloads = explicitCtors
            .Where(c => !SymbolEqualityComparer.Default.Equals(c, primary))
            .Select(c => BuildFromMethod(c, originResolver, context))
            .ToList();

        return overloads.Count > 0 ? primaryDecl with { Overloads = overloads } : primaryDecl;
    }

    /// <summary>
    /// A type without explicit constructors still runs the implicit
    /// parameterless one. It is modelled only when its base call is
    /// observable on the target side — today, a call into a BCL exception
    /// base whose default message the backend must pass (#246); a backend
    /// that emits it explicitly (Dart included) produces an empty constructor
    /// that is equivalent to the implicit one. Returns <c>null</c> otherwise.
    /// </summary>
    private static IrConstructorDeclaration? ImplicitConstructorWithObservableBaseCall(
        INamedTypeSymbol type,
        Metano.Annotations.TargetLanguage? target
    )
    {
        var construction = IrExceptionConstructionExtractor.Extract(
            ImplicitBaseConstructor(type),
            [],
            type,
            target
        );

        return construction is null
            ? null
            : new IrConstructorDeclaration([], BaseExceptionConstruction: construction);
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

    /// <summary>
    /// Whether <paramref name="constructor"/> is the C# 12 primary constructor
    /// (or a record's positional one): declared by the type declaration's
    /// parameter list rather than by a constructor declaration.
    /// </summary>
    public static bool IsPrimaryConstructor(IMethodSymbol constructor) =>
        constructor.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is TypeDeclarationSyntax { ParameterList: not null }
        );

    /// <summary>
    /// How many constructors <see cref="Extract"/> models for
    /// <paramref name="type"/> (the main one plus its overloads).
    /// </summary>
    public static int ExplicitConstructorCount(INamedTypeSymbol type) =>
        ExplicitConstructors(type).Count;

    private static List<IMethodSymbol> ExplicitConstructors(INamedTypeSymbol type) =>
        type
            .Constructors.Where(c =>
                (!c.IsImplicitlyDeclared || c.Parameters.Length > 0) && !IsRecordCopyConstructor(c)
            )
            .ToList();

    /// <summary>
    /// What deciding a parameter's promotion needs: the type, the active
    /// compilation and target, and the type's primary-constructor decision.
    /// </summary>
    private sealed record PromotionContext(
        INamedTypeSymbol Type,
        Compilation? Compilation,
        Metano.Annotations.TargetLanguage? Target,
        PrimaryConstructorPromotion Promotion
    )
    {
        /// <summary>
        /// The property <paramref name="parameter"/> is promoted to. A
        /// primary-constructor parameter follows the decision the capture scan
        /// shares (<see cref="PrimaryConstructorPromotion"/>). An explicit
        /// constructor's parameter, or any parameter without a compilation (no
        /// syntax to inspect), pairs by name with a promotion candidate,
        /// preferring an exact-case match when two properties differ only by
        /// case.
        /// </summary>
        public IPropertySymbol? PromotionTarget(IParameterSymbol parameter) =>
            Compilation is not null
            && parameter.ContainingSymbol is IMethodSymbol constructor
            && IsPrimaryConstructor(constructor)
                ? Promotion.PromotedPropertyOf(parameter)
                : NamedCandidate(parameter.Name);

        private IPropertySymbol? NamedCandidate(string name) =>
            Type.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(property =>
                    string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && PrimaryConstructorPropertyAlias.IsPromotionCandidate(property, Target)
                )
                .OrderBy(property => property.Name == name ? 0 : 1)
                .FirstOrDefault();
    }

    private static bool IsRecordCopyConstructor(IMethodSymbol ctor) =>
        ctor.IsImplicitlyDeclared
        && ctor.ContainingType.IsRecord
        && ctor.Parameters.Length == 1
        && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, ctor.ContainingType);

    private static IrConstructorDeclaration BuildFromMethod(
        IMethodSymbol ctor,
        IrTypeOriginResolver? originResolver,
        PromotionContext context
    )
    {
        var type = context.Type;
        var compilation = context.Compilation;
        var target = context.Target;

        var parameters = ctor
            .Parameters.Select(p =>
            {
                var (promotion, promotedVisibility, emittedName) = ResolvePromotionInfo(p, context);
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

        if (compilation is null)
            return new IrConstructorDeclaration(Parameters: parameters);

        var baseArguments = TryExtractBaseArguments(ctor, compilation, originResolver);

        return new IrConstructorDeclaration(
            Parameters: parameters,
            Body: TryExtractBody(ctor, compilation, originResolver),
            BaseArguments: baseArguments,
            BaseExceptionConstruction: TryExtractBaseExceptionConstruction(
                ctor,
                baseArguments ?? [],
                compilation,
                target
            ),
            ThisChain: TryExtractThisChain(ctor, originResolver, compilation, context)
        );
    }

    /// <summary>
    /// Classifies the base-constructor arguments by parameter role when the
    /// base constructor — explicit <c>: base(...)</c>, a primary-constructor
    /// base call, or the implicit parameterless one — belongs to an exception
    /// type with no target declaration (see
    /// <see cref="IrExceptionConstructionExtractor"/>). <c>: this(...)</c>
    /// chains call no base constructor and yield <c>null</c>.
    /// </summary>
    private static IrExceptionConstruction? TryExtractBaseExceptionConstruction(
        IMethodSymbol ctor,
        IReadOnlyList<IrArgument> baseArguments,
        Compilation compilation,
        Metano.Annotations.TargetLanguage? target
    )
    {
        if (FindThisCallSyntax(ctor) is not null)
            return null;

        if (FindBaseCallSyntax(ctor) is not { } baseCall)
        {
            return IrExceptionConstructionExtractor.Extract(
                ImplicitBaseConstructor(ctor.ContainingType),
                baseArguments,
                ctor.ContainingType,
                target
            );
        }

        var model = compilation.GetSemanticModel(baseCall.SyntaxTree);

        return IrExceptionConstructionExtractor.Extract(
            model.GetSymbolInfo(baseCall).Symbol as IMethodSymbol,
            baseArguments,
            ctor.ContainingType,
            target,
            ArgumentListOf(baseCall),
            model
        );
    }

    /// <summary>
    /// Captures a <c>: this(...)</c> delegation with its target constructor
    /// (built recursively — C# forbids cyclic chains), so backends without
    /// constructor chaining can inline the target.
    /// </summary>
    private static IrConstructorChain? TryExtractThisChain(
        IMethodSymbol ctor,
        IrTypeOriginResolver? originResolver,
        Compilation compilation,
        PromotionContext context
    )
    {
        if (FindThisCallSyntax(ctor) is not { } thisCall)
            return null;

        var model = compilation.GetSemanticModel(thisCall.SyntaxTree);

        if (model.GetSymbolInfo(thisCall).Symbol is not IMethodSymbol chainedCtor)
            return null;

        var arguments = MapArgumentList(
            thisCall.ArgumentList,
            thisCall.SyntaxTree,
            compilation,
            originResolver
        );

        var chainedDecl = BuildFromMethod(chainedCtor, originResolver, context);

        return new IrConstructorChain(arguments, chainedDecl);
    }

    private static SyntaxNode? DeclaringSyntax(IMethodSymbol ctor) =>
        ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

    private static ConstructorInitializerSyntax? FindThisCallSyntax(IMethodSymbol ctor) =>
        DeclaringSyntax(ctor) is ConstructorDeclarationSyntax { Initializer: { } initializer }
        && initializer.IsKind(SyntaxKind.ThisConstructorInitializer)
            ? initializer
            : null;

    /// <summary>
    /// The syntax of the explicit base-constructor call: a
    /// <c>: base(...)</c> initializer, or the C# 12 primary-constructor base
    /// (<c>class C(int x) : Base(x)</c>, whose synthesized constructor's
    /// declaring syntax is the type itself). <c>null</c> when the base call is
    /// implicit or the constructor chains through <c>: this(...)</c>.
    /// </summary>
    private static SyntaxNode? FindBaseCallSyntax(IMethodSymbol ctor) =>
        DeclaringSyntax(ctor) switch
        {
            ConstructorDeclarationSyntax { Initializer: { } initializer }
                when initializer.IsKind(SyntaxKind.BaseConstructorInitializer) => initializer,
            TypeDeclarationSyntax type => FindPrimaryBase(type.BaseList),
            _ => null,
        };

    private static ArgumentListSyntax ArgumentListOf(SyntaxNode baseCall) =>
        baseCall switch
        {
            ConstructorInitializerSyntax initializer => initializer.ArgumentList,
            PrimaryConstructorBaseTypeSyntax primaryBase => primaryBase.ArgumentList,
            _ => throw new InvalidOperationException(
                $"Unexpected base-call syntax '{baseCall.Kind()}'."
            ),
        };

    private static IMethodSymbol? ImplicitBaseConstructor(INamedTypeSymbol type) =>
        type.BaseType?.InstanceConstructors.FirstOrDefault(baseCtor =>
            baseCtor.Parameters.Length == 0
        );

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
    /// Captures the arguments of the explicit base-constructor call, if any
    /// (see <see cref="FindBaseCallSyntax"/>). The class and dispatcher
    /// bridges use these to emit the matching <c>super(...)</c> call.
    /// </summary>
    private static IReadOnlyList<IrArgument>? TryExtractBaseArguments(
        IMethodSymbol ctor,
        Compilation compilation,
        IrTypeOriginResolver? originResolver
    ) =>
        FindBaseCallSyntax(ctor) is { } baseCall
            ? MapArgumentList(
                ArgumentListOf(baseCall),
                baseCall.SyntaxTree,
                compilation,
                originResolver
            )
            : null;

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
    /// Roslyn again to render the shorthand declaration. A primary-constructor
    /// parameter is promoted only when the matching property merely exposes
    /// it and nothing writes it (<see cref="PrimaryConstructorPromotion"/>); a
    /// property computed from it keeps its own declaration.
    /// </summary>
    private static (
        IrParameterPromotion Promotion,
        IrVisibility? Visibility,
        string? EmittedName
    ) ResolvePromotionInfo(IParameterSymbol param, PromotionContext context)
    {
        if (context.PromotionTarget(param) is not { } prop)
            return (IrParameterPromotion.None, null, null);

        var promotion =
            prop.SetMethod is null || prop.SetMethod.IsInitOnly
                ? IrParameterPromotion.ReadonlyProperty
                : IrParameterPromotion.MutableProperty;
        return (
            promotion,
            IrVisibilityMapper.Map(prop.DeclaredAccessibility),
            SymbolHelper.GetNameOverride(prop, context.Target)
        );
    }
}
