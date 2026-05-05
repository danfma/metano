namespace Metano.Compiler.IR;

/// <summary>
/// Strongly-typed catalog of LINQ operators the pipe-form lowering
/// supports. Each value maps 1:1 to a function exported from
/// <c>metano-runtime/system/linq-pipe</c> and to a Roslyn method name on
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
/// and an optional <see cref="IrQueryableMeta"/> populated by the
/// expression-tree walker (Phase B / #31). Phase A leaves
/// <see cref="Queryable"/> null on every stage.
/// </summary>
public sealed record IrLinqStage(
    IrLinqOperator Operator,
    IReadOnlyList<IrExpression> Arguments,
    IrQueryableMeta? Queryable = null
);

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
