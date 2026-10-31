namespace Metano.Compiler.IR;

/// <summary>
/// A pattern used in <see cref="IrIsPatternExpression"/>, switch-expression arms, or
/// <c>case</c> labels. Patterns are a semantic test over a value — each backend
/// decides how to render the test (TypeScript: combination of <c>typeof</c>, guard
/// helpers, and narrowing; Dart: <c>is</c>/<c>case</c>).
/// <para>
/// The hierarchy today covers the most common C# patterns — constant, type, var,
/// discard. Property/relational/list/logical patterns surface as
/// <see cref="IrUnsupportedPattern"/> until a dedicated extraction pass lands.
/// </para>
/// </summary>
public abstract record IrPattern;

/// <summary>Constant pattern: <c>case 1:</c>, <c>x is null</c>, <c>x is "foo"</c>.</summary>
public sealed record IrConstantPattern(IrExpression Value) : IrPattern;

/// <summary>
/// A local a pattern declares (<c>f</c> in <c>x is Foo f</c>, <c>y</c> in
/// <c>var y</c>) and its C# type, so a backend can declare it ahead of the
/// test that assigns it.
/// </summary>
/// <param name="Name">The local's name.</param>
/// <param name="Type">The local's declared type.</param>
public sealed record IrPatternDesignator(string Name, IrTypeRef Type);

/// <summary>Type pattern: <c>x is Foo</c> / <c>x is Foo f</c>.
/// When <see cref="Designator"/> is non-null the match also binds the value
/// to that local.</summary>
public sealed record IrTypePattern(IrTypeRef Type, IrPatternDesignator? Designator = null)
    : IrPattern;

/// <summary>
/// Var pattern: <c>x is var y</c> — always matches, binds the value. As the
/// left operand of an <c>and</c> it captures the whole value, so a backend may
/// read the captured local on the right instead of the value; extraction
/// relies on this to evaluate an <c>is</c> subject once
/// (<c>F() is var $subject0 and Circle c</c>).
/// </summary>
public sealed record IrVarPattern(IrPatternDesignator Designator) : IrPattern;

/// <summary>Discard pattern: <c>_</c> — always matches, no binding.</summary>
public sealed record IrDiscardPattern : IrPattern;

/// <summary>
/// Property pattern: <c>{ X: 0, Y: var y }</c> or <c>Point { X: 0 }</c>.
/// <para>
/// <see cref="Type"/> is the optional type filter (null for bare
/// <c>{ … }</c> patterns that match any object). <see cref="Subpatterns"/>
/// names each member that must match a nested pattern (constant, type,
/// var, or further nested property patterns). <see cref="Designator"/>
/// captures the whole matched value when present (<c>is Point { } p</c>).
/// </para>
/// </summary>
public sealed record IrPropertyPattern(
    IrTypeRef? Type,
    IReadOnlyList<IrPropertySubpattern> Subpatterns,
    IrPatternDesignator? Designator = null
) : IrPattern;

/// <summary>
/// A single <c>Name: pattern</c> entry inside an <see cref="IrPropertyPattern"/>.
/// </summary>
/// <param name="MemberName">The C# member name.</param>
/// <param name="Pattern">The pattern the member's value must match.</param>
/// <param name="EmittedName">Target-resolved <c>[Name]</c> override of the
/// member, or <c>null</c> to apply the backend's naming policy.</param>
/// <param name="IsTranspiledDataMember">The member is a data member the
/// toolchain declares; see <see cref="IrMemberOrigin.IsTranspiledDataMember"/>.</param>
public sealed record IrPropertySubpattern(
    string MemberName,
    IrPattern Pattern,
    string? EmittedName = null,
    bool IsTranspiledDataMember = false
);

/// <summary>
/// Relational pattern: <c>x is &gt; 0</c>, <c>x is &lt;= 10</c>, etc.
/// </summary>
public sealed record IrRelationalPattern(IrRelationalOp Operator, IrExpression Value) : IrPattern;

/// <summary>Operators usable in a relational pattern.</summary>
public enum IrRelationalOp
{
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

/// <summary>
/// Logical pattern: <c>p1 and p2</c>, <c>p1 or p2</c>, <c>not p</c>.
/// </summary>
public sealed record IrLogicalPattern(IrLogicalOp Operator, IrPattern Left, IrPattern? Right)
    : IrPattern;

/// <summary>Operators usable in a logical pattern. <c>Not</c> is unary and uses
/// <see cref="IrLogicalPattern.Left"/> as its operand.</summary>
public enum IrLogicalOp
{
    And,
    Or,
    Not,
}

/// <summary>
/// List pattern: <c>arr is [1, 2, ..]</c>, <c>[first, .., last]</c>.
/// <see cref="SliceIndex"/> is the position of the <c>..</c> slice within
/// <see cref="Elements"/> when present (null means no slice — a fixed-arity
/// match). The slice entry is represented implicitly by <see cref="SliceIndex"/>;
/// <see cref="Elements"/> contains only the positional sub-patterns around it.
/// </summary>
public sealed record IrListPattern(
    IReadOnlyList<IrPattern> Elements,
    int? SliceIndex = null,
    IrPattern? SlicePattern = null
) : IrPattern;

/// <summary>
/// Positional pattern: <c>(0, var y)</c>, <c>Point(0, _)</c>. Used with
/// tuples and records that expose a <c>Deconstruct</c>. <see cref="Type"/>
/// is the optional type filter (null for plain tuple deconstruction).
/// </summary>
/// <param name="ElementMembers">How each element reads the matched value:
/// <c>null</c> for tuple deconstruction (elements by position); otherwise,
/// per element, the property a record's compiler-synthesized
/// <c>Deconstruct</c> reads, or <c>null</c> when the member is unknown — a
/// user-written <c>Deconstruct</c> may compute anything.</param>
/// <param name="InputMayBeNull">Whether the matched value's static type admits
/// <c>null</c> (a reference type, <c>Nullable&lt;T&gt;</c>, or an unconstrained
/// type parameter). C# positional patterns fail on <c>null</c>, so backends
/// test for it before reading elements when no type filter already does.</param>
public sealed record IrPositionalPattern(
    IrTypeRef? Type,
    IReadOnlyList<IrPattern> Elements,
    IrPatternDesignator? Designator = null,
    IReadOnlyList<IrPositionalElementMember?>? ElementMembers = null,
    bool InputMayBeNull = false
) : IrPattern;

/// <summary>
/// The property a positional pattern element reads.
/// </summary>
/// <param name="Name">The C# property name.</param>
/// <param name="EmittedName">Target-resolved <c>[Name]</c> override, or
/// <c>null</c> to apply the backend's naming policy.</param>
/// <param name="IsTranspiledDataMember">The member is a data member the
/// toolchain declares; see <see cref="IrMemberOrigin.IsTranspiledDataMember"/>.</param>
public sealed record IrPositionalElementMember(
    string Name,
    string? EmittedName,
    bool IsTranspiledDataMember = false
);

/// <summary>Placeholder for patterns the extractor doesn't yet cover
/// (parenthesized wrappers are unwrapped before reaching this fallback).
/// Backends emit a TODO comment instead of silently producing invalid code.</summary>
public sealed record IrUnsupportedPattern(string Kind) : IrPattern;
