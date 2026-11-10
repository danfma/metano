using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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

        if (!IsDispatchedSet(type))
        {
            ReportUnusedPins(explicitCtors, context.Target);

            return BuildFromMethod(primary, context.OriginResolver, promotionContext);
        }

        var named = OverloadGroupNaming.ApplyToConstructors(
            explicitCtors
                .Select(ctor =>
                    (ctor, BuildFromMethod(ctor, context.OriginResolver, promotionContext))
                )
                .ToList(),
            context.Target
        );

        var primaryIndex = explicitCtors
            .ToList()
            .FindIndex(ctor => SymbolEqualityComparer.Default.Equals(ctor, primary));

        if (primaryIndex < 0)
        {
            throw new InvalidOperationException(
                $"The primary constructor of '{type.Name}' is not one of its declared constructors."
            );
        }

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
    /// A <c>[Name]</c> on a constructor outside a dispatched set names no
    /// factory or selector (spec 013 FR-005), so it is reported.
    /// </summary>
    private static void ReportUnusedPins(
        IEnumerable<IMethodSymbol> constructors,
        Metano.Annotations.TargetLanguage? target
    )
    {
        foreach (var constructor in constructors)
            OverloadSuffixPin.Of(constructor, target)?.ReportUnusedOnConstructor();
    }

    /// <summary>
    /// Whether <paramref name="type"/>'s declared constructors form a
    /// dispatched set: two or more (spec 013 D1). Each overload of the set is
    /// named (<see cref="IrConstructorDeclaration.OverloadName"/>) and a target
    /// emits it behind one dispatcher.
    /// </summary>
    public static bool IsDispatchedSet(INamedTypeSymbol type) =>
        AllExplicitConstructors(type).Count > 1;

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
    /// The position of <paramref name="constructor"/> among its type's
    /// <see cref="AllExplicitConstructors"/> — its
    /// <see cref="IrConstructorDeclaration.DeclarationOrdinal"/> — or
    /// <c>null</c> for a constructor outside them (the implicit parameterless
    /// one, a record's copy constructor). A constructed generic type's
    /// constructor is located on its definition.
    /// </summary>
    public static int? DeclarationOrdinalOf(IMethodSymbol constructor)
    {
        var ordinal = AllExplicitConstructors(constructor.ContainingType.OriginalDefinition)
            .ToList()
            .FindIndex(declared =>
                SymbolEqualityComparer.Default.Equals(declared, constructor.OriginalDefinition)
            );

        return ordinal < 0 ? null : ordinal;
    }

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

        var baseCall = TryExtractBaseCall(ctor, compilation, context.Extraction, localNames);

        return new IrConstructorDeclaration(
            Parameters: parameters,
            Body: TryExtractBody(ctor, compilation, context.Extraction, localNames),
            BaseArguments: baseCall?.Arguments,
            BaseExceptionConstruction: TryExtractBaseExceptionConstruction(
                ctor,
                baseCall?.Arguments ?? [],
                compilation,
                target
            ),
            ThisChain: TryExtractThisChain(ctor, originResolver, compilation, context, localNames),
            Visibility: visibility,
            IsPrimary: isPrimary
        )
        {
            BaseOverload = baseCall?.Overload,
            BaseEvaluationOrder = baseCall?.EvaluationOrder,
        };
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

        extractor.ReportIfThisChainOrderIsLost(thisCall);

        // The target carries its declaration ordinal, so a backend that
        // addresses one constructor overload by it (a Dart redirect to a named
        // constructor) names the chained overload, not the first one.
        var chainedDecl = BuildFromMethod(chainedCtor, originResolver, context) with
        {
            DeclarationOrdinal = DeclarationOrdinalOf(chainedCtor) ?? 0,
        };

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
    /// Captures the base-constructor call of <paramref name="ctor"/>, if the
    /// class and dispatcher bridges must emit one: the explicit call (see
    /// <see cref="FindBaseCallSyntax"/>) with its source arguments, or — when
    /// its base overload is one a target selects exactly
    /// (<see cref="DispatchedConstructorCalls.BaseCallTarget"/>) — with
    /// Roslyn's bound arguments and that overload; an implicit call only in
    /// the latter case, since a bare <c>super()</c> needs no IR.
    /// </summary>
    private static BaseConstructorCall? TryExtractBaseCall(
        IMethodSymbol ctor,
        Compilation compilation,
        IrExtractionContext context,
        LocalBindingNames localNames
    )
    {
        if (FindBaseCallSyntax(ctor) is { } baseCall)
            return ExtractExplicitBaseCall(baseCall, compilation, context, localNames);

        if (FindImplicitBaseCall(ctor, compilation) is not { } implicitCall)
            return null;

        return ExtractSelectedBaseCall(implicitCall, compilation, context, localNames) is { } bound
            ? BaseConstructorCall.Of(bound)
            : null;
    }

    private static BaseConstructorCall ExtractExplicitBaseCall(
        SyntaxNode baseCall,
        Compilation compilation,
        IrExtractionContext context,
        LocalBindingNames localNames
    )
    {
        var model = compilation.GetSemanticModel(baseCall.SyntaxTree);

        if (model.GetOperation(baseCall) is IInvocationOperation invocation)
        {
            var bound = ExtractSelectedBaseCall(invocation, compilation, context, localNames);

            if (bound is not null)
                return BaseConstructorCall.Of(bound);
        }

        var extractor = new IrExpressionExtractor(model, context, localNames);

        var arguments = extractor.ExtractBaseArguments(
            ArgumentListOf(baseCall),
            model.GetSymbolInfo(baseCall).Symbol as IMethodSymbol
        );

        return new BaseConstructorCall(arguments, Overload: null)
        {
            EvaluationOrder = extractor.SourceOrderOf(model.GetOperation(baseCall), arguments),
        };
    }

    /// <summary>
    /// The base call of <paramref name="invocation"/> lowered to its exact
    /// overload (spec 013 FR-017); <c>null</c> when the base keeps its
    /// dispatcher or an argument cannot be bound.
    /// </summary>
    private static BoundOverloadCall? ExtractSelectedBaseCall(
        IInvocationOperation invocation,
        Compilation compilation,
        IrExtractionContext context,
        LocalBindingNames localNames
    )
    {
        var target = DispatchedConstructorCalls.BaseCallTarget(invocation.TargetMethod, context);

        if (target is null)
            return null;

        var model = compilation.GetSemanticModel(invocation.Syntax.SyntaxTree);
        var extractor = new IrExpressionExtractor(model, context, localNames);

        return extractor.ExtractBoundCall(invocation, target);
    }

    /// <summary>
    /// The implicit <c>base()</c> call of a declared constructor with no
    /// initializer, as Roslyn binds it.
    /// </summary>
    private static IInvocationOperation? FindImplicitBaseCall(
        IMethodSymbol ctor,
        Compilation compilation
    )
    {
        if (DeclaringSyntax(ctor) is not ConstructorDeclarationSyntax { Initializer: null } syntax)
            return null;

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);

        return
            model.GetOperation(syntax)
                is IConstructorBodyOperation
                {
                    Initializer: IExpressionStatementOperation
                    {
                        Operation: IInvocationOperation call
                    },
                }
            ? call
            : null;
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

    /// <summary>
    /// A constructor's base call: its arguments and, when a target selects
    /// the base overload exactly, that overload — with which the arguments
    /// are Roslyn's bound ones.
    /// </summary>
    private sealed record BaseConstructorCall(
        IReadOnlyList<IrArgument> Arguments,
        IrSelectedOverload? Overload
    )
    {
        /// <summary>
        /// The order to keep for the arguments of a call bound to no
        /// overload (<see cref="IrConstructorDeclaration.BaseEvaluationOrder"/>).
        /// </summary>
        public IReadOnlyList<int>? EvaluationOrder { get; init; }

        public static BaseConstructorCall Of(BoundOverloadCall bound) =>
            new(bound.Arguments, bound.Overload);
    }
}
