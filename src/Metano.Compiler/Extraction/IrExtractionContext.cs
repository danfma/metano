using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Everything an IR extractor needs beyond its syntax and semantic model:
/// the cross-package type-origin resolver, the compilation (null in
/// shape-only mode), the target's overload-grouping policy (which names the
/// language target), and the target's local-naming
/// decision the local-name disambiguator maps bindings through (#398). One
/// immutable object threads through every extractor — type, member, module
/// function, statement, expression — replacing the
/// <c>originResolver / compilation / target / localNaming</c> parameter
/// clump, so a new extraction input reaches every nested extractor by
/// construction instead of by threading one more optional parameter.
/// </summary>
public sealed record IrExtractionContext(
    IrTypeOriginResolver? OriginResolver = null,
    Compilation? Compilation = null,
    OverloadGroupingPolicy? GroupingPolicy = null,
    LocalNaming? LocalNaming = null
)
{
    /// <summary>
    /// What the overload groups depend on besides the symbols (spec 013
    /// FR-039): the language target and the names it emits synthesized
    /// value-equality members under. A target passes its single instance;
    /// without one there is no target and no synthesized member joins a group.
    /// </summary>
    public OverloadGroupingPolicy GroupingPolicy { get; init; } =
        GroupingPolicy ?? OverloadGroupingPolicy.For(null);

    /// <summary>The language target, the one <see cref="GroupingPolicy"/> names.</summary>
    public Metano.Annotations.TargetLanguage? Target => GroupingPolicy.Target;

    /// <summary>The numeric conversions the core makes explicit for <see cref="Target"/>.</summary>
    internal NumericLoweringRules NumericLowering => NumericLoweringRules.For(Target);

    /// <summary>
    /// The port through which a target says which types it emits with an
    /// addressable form per constructor overload — TypeScript a selector and,
    /// for a concrete type, a factory (spec 013 FR-007, FR-008, DECIDED-10),
    /// Dart a named constructor (FR-022, DECIDED-14) — so the extractor
    /// lowers calls into them to the overload Roslyn resolved
    /// (<see cref="DispatchedConstructorCalls"/>). <c>null</c>, the default,
    /// for a target that emits none: every construction and base call then
    /// keeps its source arguments and the target's dispatcher.
    /// </summary>
    public Func<INamedTypeSymbol, bool>? EmitsConstructorSelectors { get; init; }

    /// <summary>
    /// Whether the target emits an addressable fast path per overload of a
    /// dispatched operator group (spec 013 FR-033), so the extractor binds an
    /// operator application to the overload Roslyn resolved. Opt-in, like
    /// <see cref="EmitsConstructorSelectors"/>: a target that does not
    /// declare the fast paths keeps the source operator. TypeScript sets it
    /// (<c>Money.__addMoneyInt32(a, b)</c>); Dart declares one
    /// <c>operator +</c> per class and leaves it unset. Method, <c>base.M</c>,
    /// method-group and module-function call sites bind on every target: each
    /// names the fast paths of those groups (#549).
    /// </summary>
    public bool EmitsOperatorFastPaths { get; init; }

    /// <summary>
    /// Whether the target declares an addressable fast path for a method of
    /// a <c>[Branded]</c> / <c>[InlineWrapper]</c> type when its group is
    /// dispatched (issue #569), so the extractor binds a call or method
    /// group to the overload Roslyn resolved, as it does for any class. A
    /// port, like <see cref="EmitsConstructorSelectors"/>: how a branded type
    /// lowers — and so which of its members exist to be called — is the
    /// target's own decision. TypeScript answers for the static methods its
    /// companion namespace exports (<c>Tag.sU</c> beside the dispatcher
    /// <c>Tag.s</c>) and for the methods of a wrapper it lowers to a class.
    /// <c>null</c> for a target that declares one member per group: every
    /// such call keeps the source form (Dart).
    /// </summary>
    public Func<IMethodSymbol, bool>? EmitsBrandedFastPath { get; init; }

    /// <summary>
    /// The port through which a target says whether it gives an exception type
    /// without a declaration of its own (a BCL one, mostly) a runtime class
    /// that tells it apart (spec 011 FR-014): TypeScript answers from its
    /// <c>[ExportFromBcl]</c> mapping of the <c>metano-runtime</c> exception
    /// classes. Where it answers <c>false</c>, the extractor reports
    /// <c>MS0001</c> at a construction (the target throws its
    /// <c>Exception</c>) and <c>MS0045</c> at a type test (<c>is</c>,
    /// <c>as</c>, a pattern, a <c>catch</c> clause), which would otherwise take
    /// every exception. <c>null</c>, the default, for a target that tests
    /// every exception type natively (Dart): nothing is reported.
    /// </summary>
    public Func<INamedTypeSymbol, bool>? HasExceptionRuntimeClass { get; init; }

    /// <summary>
    /// A target capability: whether the target keeps C#'s evaluation order
    /// for a call, construction or <c>: this(...)</c> chain whose named
    /// arguments are out of parameter order
    /// (<see cref="IR.IrSelectedOverload.EvaluationOrder"/> when it is bound
    /// to an overload, <see cref="IR.IrCallExpression.EvaluationOrder"/>
    /// otherwise), binding them in source order. Opt-in. A target that leaves it unset
    /// passes them in parameter order, which cannot differ from C# while at
    /// most one evaluation is order-sensitive; with two or more the extractor
    /// reports <c>MS0001</c> at the call.
    /// </summary>
    public bool KeepsArgumentEvaluationOrder { get; init; }

    /// <summary>
    /// The port through which a target says whether a declarative mapping
    /// (<c>[MapMethod]</c>) of its own lowers the overload a call to a BCL
    /// method binds to when the extracted call passes the given number of
    /// arguments (the count of the IR arguments its BCL mapper filters on:
    /// the written ones, or one per parameter when an argument is named), by
    /// the same overload filter its BCL mapper applies, so the extractor
    /// reports a <c>System.Linq</c> or <c>System.MemoryExtensions</c> method
    /// that nothing lowers instead of leaving it a member call on the
    /// receiver (#648, #657), and an intrinsic lowering (<c>decimal.Parse</c>)
    /// yields to the target's own mapping (#651). <c>null</c>, the default,
    /// for a target that reports none of these calls: every such call keeps
    /// its source form.
    /// <para>
    /// TODO(#657): the answer ignores <c>WhenArg0StringEquals</c>, which the
    /// mapper tests on the lowered arguments, so a call whose only entries
    /// carry that filter counts as lowered. It describes a TypeScript
    /// decision and belongs to that target together with the runtime
    /// signature table.
    /// </para>
    /// </summary>
    public Func<IMethodSymbol, int, bool>? LowersByDeclarativeMapping { get; init; }

    /// <summary>
    /// The port through which a target says which format strings it honors
    /// (baseline FR-051, #323): its runtime's numeric formatters and the
    /// formats a BCL type prints as its clause-free text. <c>null</c>, the default,
    /// fails closed: the extractor reports every interpolation format or
    /// alignment clause and every <c>ToString(format)</c> it governs, naming
    /// <see cref="Target"/>. TypeScript sets it (<c>metano-runtime</c>'s
    /// formatters); Dart honors no clause yet and leaves it unset.
    /// </summary>
    public IFormatStringCapability? FormatStrings { get; init; }

    /// <summary>
    /// The context of a constant parameter default: the origin resolver
    /// alone. It names no local, so it needs no local naming, and it drops
    /// the target and the compilation as defaults always have: a constant
    /// needs neither a target-specific rewrite (the Temporal comparisons) nor
    /// a second semantic model. The target capabilities
    /// (<see cref="EmitsConstructorSelectors"/>,
    /// <see cref="EmitsOperatorFastPaths"/>,
    /// <see cref="EmitsBrandedFastPath"/>,
    /// <see cref="KeepsArgumentEvaluationOrder"/>,
    /// <see cref="HasExceptionRuntimeClass"/>,
    /// <see cref="FormatStrings"/>) are dropped on purpose: each one binds a
    /// constructor call, a user-defined operator, a method call, a call with
    /// named arguments or a formatted value, or answers for an exception
    /// construction or type test, and a C# default value is a constant,
    /// <c>default</c> or a parameterless struct construction, which holds
    /// none of them.
    /// </summary>
    public IrExtractionContext ForConstantDefault() => new(OriginResolver);

    /// <summary>Shape-only extraction: no resolver, compilation or target.</summary>
    public static IrExtractionContext Empty { get; } = new();
}
