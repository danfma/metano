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
    private readonly bool _keepsCallEvaluationOrder;

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
    /// Whether the target prints the user-defined operator shapes as the
    /// source wrote them, as it did before baseline FR-077 / FR-079 gave
    /// them nodes (Dart, transitionally; see <c>DartUserOperatorForms</c>).
    /// Such a target gets no <c>MS0001</c> for the shapes no target calls
    /// yet (a condition tested through a user <c>true</c> operator, a user
    /// <c>&amp;&amp;</c> / <c>||</c>, an operator or conversion of a
    /// <c>[PlainObject]</c> type, a C# 14 instance compound or increment
    /// operator), and a compound assignment through an operator the
    /// extraction does not apply keeps its instance-helper call
    /// (<c>x = x.$add(y)</c>), so its output and diagnostics stay as they were.
    /// </summary>
    internal bool KeepsUserOperatorSourceForms => Target == Metano.Annotations.TargetLanguage.Dart;

    /// <summary>
    /// Whether the target keeps the operations a <c>dynamic</c> value binds at
    /// run time in the symbol-less form the extraction gave them before
    /// spec 011 FR-030 (Dart until after 3.0.0, D28): no dynamic nodes, no
    /// runtime numeric conversions, no late-bound dispatch and no
    /// <c>MS0053</c>, so its output and diagnostics stay as they were. Only
    /// the <c>dynamic</c> type itself is mapped, which Dart renders as before.
    /// </summary>
    internal bool KeepsDynamicSourceForms => Target == Metano.Annotations.TargetLanguage.Dart;

    /// <summary>
    /// Whether the target keeps <c>GetType()</c> and <c>typeof</c> in the form
    /// the extraction gave them before spec 011 stage 011b (Dart until after
    /// 3.0.0, D28): <c>GetType()</c> as an ordinary call and <c>typeof</c> as an
    /// unsupported expression (MS0001), with no runtime-type node, so its output
    /// and diagnostics stay as they were.
    /// </summary>
    internal bool KeepsReflectionSourceForms => Target == Metano.Annotations.TargetLanguage.Dart;

    /// <summary>
    /// Whether the target can assign a local inside a test expression and go
    /// on testing (an assignment in a comma expression:
    /// <c>($subject0 = next(), true) &amp;&amp; …</c>), so a pattern that binds
    /// nothing can capture a subject it reads more than once in a synthesized
    /// designator (#735). Dart has no such form and keeps the uncaptured one.
    /// </summary>
    internal bool HasAssignmentInTestForm => Target != Metano.Annotations.TargetLanguage.Dart;

    /// <summary>
    /// Whether the target's BCL collections are its native ones (Dart until
    /// spec 011 FR-038, D28): such a target gets the pre-011c lowering of every
    /// collection construct (the 2.x element-access reads, writes and
    /// compound updates, <c>r[length - n]</c>, no array-to-interface
    /// conversion, the 2.x diagnostics), so its output and diagnostics stay as
    /// they were; every other target gets the structured nodes of stage 011c
    /// (spec 011 data-model § 2, W4-31).
    /// </summary>
    internal bool KeepsNativeCollections => KeepsNativeCollectionsFor(Target);

    /// <summary>
    /// Whether the target keeps C#'s argument evaluation order on calls and
    /// constructions. Redirecting <c>: this(...)</c> chains still follow
    /// <see cref="KeepsArgumentEvaluationOrder"/>.
    /// </summary>
    public bool KeepsCallEvaluationOrder
    {
        get => KeepsArgumentEvaluationOrder || _keepsCallEvaluationOrder;
        init => _keepsCallEvaluationOrder = value;
    }

    /// <summary>
    /// Post-extraction fixpoint analysis of reified type parameters (spec 011
    /// FR-024, stage 011d).
    /// </summary>
    public Analysis.ReifiedTypeAnalysisResult ReifiedTypeAnalysis { get; init; } =
        Analysis.ReifiedTypeParameterAnalysis.Disabled;

    /// <summary>
    /// The port through which a target says that it cannot print a value of a
    /// type at a default-text site (an interpolation hole, a concatenation
    /// operand, a <c>ToString()</c> that binds to <c>object</c>'s or a record's
    /// synthesized one): it returns the reason, which the extractor reports as
    /// <c>MS0001</c> (a warning) at the site, or <c>null</c> when the target
    /// prints the type. TypeScript answers for a <c>[PlainObject]</c> type, which
    /// has no class (baseline FR-094.4). <c>null</c>, the default, for a target
    /// that prints every type (Dart): nothing is reported.
    /// </summary>
    public Func<INamedTypeSymbol, string?>? UnprintableTextReason { get; init; }

    /// <summary>
    /// <see cref="KeepsNativeCollections"/> for a code path that holds only the
    /// target language (the type mapper, the overload-group reporters).
    /// </summary>
    internal static bool KeepsNativeCollectionsFor(Metano.Annotations.TargetLanguage? target) =>
        target == Metano.Annotations.TargetLanguage.Dart;

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
    /// The port through which a target says, best effort from the symbol, the
    /// name it declares a member under (its <c>[Name]</c> or its naming
    /// policy: TypeScript camelCases <c>Total</c> to <c>total</c>), so the
    /// extractor can warn when a
    /// verbatim <c>dynamic</c> access on an <c>AsDynamic()</c> result names a
    /// member of the receiver's static type that is emitted under another
    /// name (<c>MS0057</c>, spec 011 FR-036). <c>null</c>, the default, for a
    /// target that reports nothing.
    /// </summary>
    public Func<ISymbol, string>? EmittedMemberName { get; init; }

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
    /// <see cref="EmitsBrandedFastPath"/>,
    /// <see cref="KeepsArgumentEvaluationOrder"/>,
    /// <see cref="HasExceptionRuntimeClass"/>,
    /// <see cref="FormatStrings"/>) are dropped on purpose: each one binds a
    /// constructor call, a method call, a call with
    /// named arguments or a formatted value, or answers for an exception
    /// construction or type test, and a C# default value is a constant,
    /// <c>default</c> or a parameterless struct construction, which holds
    /// none of them.
    /// </summary>
    public IrExtractionContext ForConstantDefault() => new(OriginResolver);

    /// <summary>Shape-only extraction: no resolver, compilation or target.</summary>
    public static IrExtractionContext Empty { get; } = new();
}
