namespace Metano.Compiler.IR;

/// <summary>
/// Strongly-typed catalog of LINQ operators the pipe-form lowering
/// supports. Each value maps 1:1 to a function exported from
/// <c>metano-runtime/system/linq</c> and to a Roslyn method name on
/// <c>System.Linq.Enumerable</c> / <c>System.Linq.Queryable</c>.
/// <para>
/// The two-way translation lives in helpers attached to this enum so the
/// extractor (Roslyn name → enum) and the bridge (enum → emitted name)
/// share a single source of truth — no string typos can creep in.
/// </para>
/// </summary>
public enum IrLinqOperator
{
    // ── Composition (lazy) ──
    Where,
    Select,
    SelectMany,
    OrderBy,
    OrderByDescending,
    ThenBy,
    ThenByDescending,
    GroupBy,
    Take,
    Skip,
    TakeWhile,
    SkipWhile,
    Distinct,
    DistinctBy,
    Concat,
    Append,
    Prepend,
    Reverse,
    Zip,

    // ── Terminals (eager) ──
    ToArray,
    ToMap,
    ToSet,
    First,
    FirstOrDefault,
    Last,
    LastOrDefault,
    Single,
    SingleOrDefault,
    Any,
    All,
    Count,
    Sum,
    Min,
    Max,
    MinBy,
    MaxBy,
    Average,
    Contains,
    Aggregate,
}

/// <summary>
/// Lowering of a full C# LINQ chain into the pipe form
/// <c>linq(source, op1(...), op2(...), opN(...))</c>. Built by the
/// extractor when it sees the outermost call of a chain whose every
/// stage is a known <see cref="IrLinqOperator"/>; consumed by the
/// TypeScript bridge as a single <c>linq()</c> invocation.
/// </summary>
/// <param name="Source">The chain root — first non-LINQ receiver.</param>
/// <param name="Stages">Ordered list of LINQ stages from outermost
/// receiver-to-terminal direction. The bridge emits one TS call per
/// entry inside the surrounding <c>linq(...)</c> wrapper.</param>
public sealed record IrLinqChain(IrExpression Source, IReadOnlyList<IrLinqStage> Stages)
    : IrExpression;

/// <summary>
/// Single stage of an <see cref="IrLinqChain"/>. Each stage carries the
/// operator identity, the lowered argument IR (lambdas, literals, etc),
/// an optional <see cref="IrQueryableMeta"/> populated by the
/// expression-tree walker (Phase B / #31; Phase A leaves
/// <see cref="Queryable"/> null on every stage) and, for a numeric stage
/// over 64-bit values, their <see cref="IrLinqSixtyFourBitValues"/>.
/// </summary>
public sealed record IrLinqStage(
    IrLinqOperator Operator,
    IReadOnlyList<IrExpression> Arguments,
    IrQueryableMeta? Queryable = null,
    IrLinqSixtyFourBitValues? SixtyFourBitValues = null
);

/// <summary>
/// The 64-bit values a numeric stage works on (spec 012 FR-036, FR-041):
/// the values a <c>Sum</c>, <c>Average</c>, <c>Min</c> or <c>Max</c>
/// aggregates (<c>long</c>, <c>long?</c>, <c>ulong</c>, …), or the
/// <c>long</c> count of a <c>LongCount</c>, so a target that represents
/// them apart from other numbers can pick its terminal.
/// </summary>
public sealed record IrLinqSixtyFourBitValues(IrNumericKind Kind, bool IsNullable);

/// <summary>
/// Compiler-side bundle threaded into a stage's
/// <see cref="IrLinqStage.Queryable"/> field when an opt-in queryable
/// surface is detected. <see cref="Tree"/> is the IR expression tree
/// for the lambda body; <see cref="Captures"/> names every closure
/// capture the body references so the bridge can emit a runtime lookup
/// table on the consumer side. Both halves land in the runtime
/// <c>QueryableMeta</c> shape (<see cref="ExprTree"/> + <c>captures</c>
/// record) on the TypeScript output.
/// </summary>
public sealed record IrQueryableMeta(
    IrExprTreeNode Tree,
    IReadOnlyList<IrQueryableCapture>? Captures = null
);

/// <summary>One captured local referenced from inside a lambda body.</summary>
public sealed record IrQueryableCapture(string Name, IrExpression Value, IrTypeRef? Type);
