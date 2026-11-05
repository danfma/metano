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
        IrExtractionContext? context = null
    )
    {
        context ??= IrExtractionContext.Empty;

        return Extract(
            type,
            context,
            PrimaryConstructorPromotion.For(type, context.Compilation, context.Target)
        );
    }

    /// <summary>
    /// <see cref="Extract(INamedTypeSymbol, IrExtractionContext?)"/> with the
    /// type's primary-constructor <paramref name="promotion"/> already
    /// decided, so a caller that also scans captures shares it.
    /// </summary>
    internal static IrConstructorDeclaration? Extract(
        INamedTypeSymbol type,
        IrExtractionContext context,
        PrimaryConstructorPromotion promotion
    )
    {
        var explicitCtors = AllExplicitConstructors(type);
        var primary = FindPrimaryConstructor(type);

        if (primary is null)
            return ImplicitConstructorWithObservableBaseCall(type, context.Target);

        var promotionContext = new PromotionContext(type, context, promotion);

        if (explicitCtors.Count < 2)
            return BuildFromMethod(primary, context.OriginResolver, promotionContext);

        var named = OverloadGroupNaming.ApplyToConstructors(
            explicitCtors
                .Select(ctor =>
                    (ctor, BuildFromMethod(ctor, context.OriginResolver, promotionContext))
                )
                .ToList()
        );

        var primaryIndex = explicitCtors
            .ToList()
            .FindIndex(ctor => SymbolEqualityComparer.Default.Equals(ctor, primary));

        // Other constructors become overloads (without nested Overloads of their own).
        var overloads = named.Where((_, index) => index != primaryIndex).ToList();

        return named[primaryIndex] with
        {
            Overloads = overloads,
        };
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
    /// constructor: the record positional or C# 12 primary constructor when
    /// the type has one (every other constructor chains to it), else the one
    /// with the most parameters. Promotions are keyed off it, and any target
    /// building positional <c>new T(...)</c> calls must follow its parameter
    /// order. Returns <c>null</c> when the type only has the implicit
    /// parameterless constructor (or the record copy constructor).
    /// </summary>
    public static IMethodSymbol? FindPrimaryConstructor(INamedTypeSymbol type)
    {
        var constructors = AllExplicitConstructors(type);

        return constructors.FirstOrDefault(IsPrimaryConstructor)
            ?? constructors.OrderByDescending(c => c.Parameters.Length).FirstOrDefault();
    }

    /// <summary>
    /// Whether <paramref name="constructor"/> is the C# 12 primary constructor
    /// (or a record's positional one): declared by the type declaration's
    /// parameter list rather than by a constructor declaration. Read from
    /// metadata, only a record's positional constructor is recognized, by the
    /// <c>Deconstruct</c> the compiler synthesizes with the same parameter types.
    /// </summary>
    public static bool IsPrimaryConstructor(IMethodSymbol constructor) =>
        IsFromMetadata(constructor)
            ? IsPositionalRecordConstructorInMetadata(constructor)
            : constructor.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax() is TypeDeclarationSyntax { ParameterList: not null }
            );

    /// <summary>
    /// The accessibility the source declares for <paramref name="ctor"/>. A
    /// primary (record positional or C# 12) constructor has no place to
    /// declare one and is public: the <c>protected</c> Roslyn synthesizes for
    /// an abstract type's primary constructor only restates that the type
    /// cannot be instantiated.
    /// <para>
    /// Read from metadata (a binary reference: a DLL or a package; a project
    /// reference loads as source), the implicit constructor is
    /// indistinguishable from a declared one: an abstract type's
    /// <c>protected</c> parameterless constructor, the one C# synthesizes, is
    /// taken as implicit and public.
    /// </para>
    /// </summary>
    public static IrVisibility DeclaredVisibility(IMethodSymbol ctor) =>
        IsPrimaryConstructor(ctor) || IsImplicitAbstractConstructorInMetadata(ctor)
            ? IrVisibility.Public
            : IrVisibilityMapper.Map(ctor.DeclaredAccessibility);

    /// <summary>
    /// The constructors <see cref="Extract"/> models for
    /// <paramref name="type"/> (the main one plus its overloads), whatever
    /// their accessibility, in declaration order: every declared instance
    /// constructor except the implicit parameterless one and a record's copy
    /// constructor. A static constructor is not one of them.
    /// </summary>
    public static IReadOnlyList<IMethodSymbol> AllExplicitConstructors(INamedTypeSymbol type) =>
        type
            .InstanceConstructors.Where(c =>
                (!c.IsImplicitlyDeclared || c.Parameters.Length > 0) && !IsRecordCopyConstructor(c)
            )
            .ToList();

    /// <summary>
    /// What deciding a parameter's promotion needs: the type, the active
    /// compilation and target, the type's primary-constructor decision, and
    /// the target's local-naming policy for constructor-body bindings (#398).
    /// </summary>
    private sealed record PromotionContext(
        INamedTypeSymbol Type,
        IrExtractionContext Extraction,
        PrimaryConstructorPromotion Promotion
    )
    {
        public Compilation? Compilation => Extraction.Compilation;

        public Metano.Annotations.TargetLanguage? Target => Extraction.Target;

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
            IsInspectablePrimaryParameter(parameter)
                ? Promotion.PromotedPropertyOf(parameter)
                : PrimaryConstructorPropertyAlias
                    .CandidatesNamed(Type, parameter.Name, Target)
                    .FirstOrDefault();

        private bool IsInspectablePrimaryParameter(IParameterSymbol parameter) =>
            Compilation is not null
            && parameter.ContainingSymbol is IMethodSymbol constructor
            && IsPrimaryConstructor(constructor);
    }

    /// <summary>
    /// A record's synthesized copy constructor; read from metadata, where
    /// nothing is implicitly declared, any one taking the record itself.
    /// </summary>
    private static bool IsRecordCopyConstructor(IMethodSymbol ctor) =>
        (ctor.IsImplicitlyDeclared || IsFromMetadata(ctor))
        && ctor.ContainingType.IsRecord
        && ctor.Parameters.Length == 1
        && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, ctor.ContainingType);

    /// <summary>
    /// Whether <paramref name="ctor"/> comes from a binary reference (a DLL or
    /// a package) and so has no syntax: nothing there is implicitly declared,
    /// and a primary constructor has no parameter list to recognize it by.
    /// </summary>
    private static bool IsFromMetadata(IMethodSymbol ctor) =>
        ctor.DeclaringSyntaxReferences.IsEmpty;

    private static bool IsPositionalRecordConstructorInMetadata(IMethodSymbol ctor) =>
        ctor.ContainingType.IsRecord
        && ctor.ContainingType.GetMembers("Deconstruct")
            .OfType<IMethodSymbol>()
            .Any(deconstruct =>
                deconstruct
                    .Parameters.Select(parameter => parameter.Type)
                    .SequenceEqual(
                        ctor.Parameters.Select(parameter => parameter.Type),
                        SymbolEqualityComparer.Default
                    )
            );

    private static bool IsImplicitAbstractConstructorInMetadata(IMethodSymbol ctor) =>
        IsFromMetadata(ctor)
        && ctor.ContainingType.IsAbstract
        && ctor.Parameters.IsEmpty
        && ctor.DeclaredAccessibility == Accessibility.Protected;

    private static IrConstructorDeclaration BuildFromMethod(
        IMethodSymbol ctor,
        IrTypeOriginResolver? originResolver,
        PromotionContext context
    )
    {
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
                        ParameterDefaultValues.Extract(p, context.Extraction),
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

        var visibility = DeclaredVisibility(ctor);
        var isPrimary = IsPrimaryConstructor(ctor);

        if (compilation is null)
            return new IrConstructorDeclaration(
                Parameters: parameters,
                Visibility: visibility,
                IsPrimary: isPrimary
            );

        // One map per constructor declaration (#398): the walk's root is the
        // whole declaration, so the `: base(...)` / `: this(...)` argument
        // lambdas and the body share it — a collision renames one name every
        // half agrees on.
        var localNames = LocalBindingDisambiguator.ForMember(ctor, context.Extraction);

        var baseArguments = TryExtractBaseArguments(
            ctor,
            compilation,
            context.Extraction,
            localNames
        );

        return new IrConstructorDeclaration(
            Parameters: parameters,
            Body: TryExtractBody(ctor, compilation, context.Extraction, localNames),
            BaseArguments: baseArguments,
            BaseExceptionConstruction: TryExtractBaseExceptionConstruction(
                ctor,
                baseArguments ?? [],
                compilation,
                target
            ),
            ThisChain: TryExtractThisChain(ctor, originResolver, compilation, context, localNames),
            Visibility: visibility,
            IsPrimary: isPrimary
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
        PromotionContext context,
        LocalBindingNames localNames
    )
    {
        if (FindThisCallSyntax(ctor) is not { } thisCall)
            return null;

        var model = compilation.GetSemanticModel(thisCall.SyntaxTree);

        if (model.GetSymbolInfo(thisCall).Symbol is not IMethodSymbol chainedCtor)
            return null;

        var extractor = new IrExpressionExtractor(model, context.Extraction, localNames);
        var arguments = extractor.ExtractInitializerArguments(thisCall.ArgumentList, callee: null);

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
        IrExtractionContext context,
        LocalBindingNames localNames
    )
    {
        var syntax =
            ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as ConstructorDeclarationSyntax;
        if (syntax is null || (syntax.Body is null && syntax.ExpressionBody is null))
            return null;

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var extractor = new IrStatementExtractor(model, context, inheritedLocalNames: localNames);

        return extractor.ExtractBody(syntax.Body, syntax.ExpressionBody, isVoid: true).Statements;
    }

    /// <summary>
    /// Captures the arguments of the explicit base-constructor call, if any
    /// (see <see cref="FindBaseCallSyntax"/>). The class and dispatcher
    /// bridges use these to emit the matching <c>super(...)</c> call.
    /// </summary>
    private static IReadOnlyList<IrArgument>? TryExtractBaseArguments(
        IMethodSymbol ctor,
        Compilation compilation,
        IrExtractionContext context,
        LocalBindingNames localNames
    )
    {
        if (FindBaseCallSyntax(ctor) is not { } baseCall)
            return null;

        var model = compilation.GetSemanticModel(baseCall.SyntaxTree);
        var extractor = new IrExpressionExtractor(model, context, localNames);

        return extractor.ExtractInitializerArguments(
            ArgumentListOf(baseCall),
            model.GetSymbolInfo(baseCall).Symbol as IMethodSymbol
        );
    }

    private static PrimaryConstructorBaseTypeSyntax? FindPrimaryBase(BaseListSyntax? baseList) =>
        baseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault();

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
            IrVisibilityMapper.MapMember(prop),
            SymbolHelper.GetNameOverride(prop, context.Target)
        );
    }
}
