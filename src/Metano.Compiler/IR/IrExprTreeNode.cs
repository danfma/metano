namespace Metano.Compiler.IR;

/// <summary>
/// IR shape of a lambda body captured for IQueryable analysis. Mirrors
/// the runtime <c>ExprTree</c> union in
/// <c>metano-runtime/system/linq/expr-tree.ts</c> — the bridge
/// lowers each variant to the matching TS object literal.
///
/// <para>
/// Subset (Phase B / #31, extended in #206): param, capture, literal,
/// member, call, binary, unary, conditional, new, lambda. Phase B's
/// walker emits MS0024 when the lambda body uses constructs outside
/// this list.
/// </para>
/// </summary>
public abstract record IrExprTreeNode;

/// <summary>Reference to a lambda parameter — `(u) =&gt; …` introduces `u`.</summary>
public sealed record IrExprParam(string Name, IrTypeRef? Type = null) : IrExprTreeNode;

/// <summary>Reference to a closure-captured local. Provider resolves via
/// the descriptor's <c>captures</c> record at runtime.</summary>
public sealed record IrExprCapture(string Name, IrTypeRef? Type = null) : IrExprTreeNode;

/// <summary>Constant value: literal, default, or compile-time-folded
/// constant local.</summary>
public sealed record IrExprLiteral(object? Value, IrTypeRef? Type = null) : IrExprTreeNode;

/// <summary>Property / field access: <c>target.member</c>. Member name
/// is the camelCase TS identifier the consumer file would reference at
/// runtime — providers analyzing the post-compile object see that name.</summary>
public sealed record IrExprMember(IrExprTreeNode Target, string Member) : IrExprTreeNode;

/// <summary>Method or extension call: <c>target.method(args)</c> or
/// <c>static(args)</c>. <see cref="Target"/> is null for a free
/// function call.</summary>
public sealed record IrExprCall(
    IrExprTreeNode? Target,
    string Method,
    IReadOnlyList<IrExprTreeNode> Args
) : IrExprTreeNode;

/// <summary>Binary op: <c>left op right</c>. Operator is a TS-style
/// token (<c>==</c>, <c>!=</c>, <c>&amp;&amp;</c>, etc) so the bridge
/// can emit it verbatim.</summary>
public sealed record IrExprBinary(string Op, IrExprTreeNode Left, IrExprTreeNode Right)
    : IrExprTreeNode;

/// <summary>Unary op: <c>op operand</c>.</summary>
public sealed record IrExprUnary(string Op, IrExprTreeNode Operand) : IrExprTreeNode;

/// <summary>Ternary: <c>condition ? whenTrue : whenFalse</c>.</summary>
public sealed record IrExprConditional(
    IrExprTreeNode Condition,
    IrExprTreeNode WhenTrue,
    IrExprTreeNode WhenFalse
) : IrExprTreeNode;

/// <summary>
/// Object-creation: <c>new T(args)</c> with optional initializer block
/// <c>{ Member = value, ... }</c>. <see cref="Initializers"/> is null
/// (not just empty) when the syntax uses no initializer block, so the
/// bridge can skip the optional <c>initializers</c> property entirely.
/// </summary>
public sealed record IrExprNew(
    IrTypeRef? Type,
    IReadOnlyList<IrExprTreeNode> Args,
    IReadOnlyList<IrExprNewInitializer>? Initializers = null
) : IrExprTreeNode;

/// <summary>
/// Single <c>Member = value</c> assignment inside an
/// <c>ObjectInitializerExpression</c>. <see cref="Member"/> is the C#
/// identifier — the bridge applies the same member-casing rule as
/// <see cref="IrExprMember"/> so the emitted name matches the lowered
/// TS surface.
/// </summary>
public sealed record IrExprNewInitializer(string Member, IrExprTreeNode Value);

/// <summary>
/// Nested lambda: <c>(p, …) => body</c>. Captures and params bind in the
/// outer walker's scope; the runtime visitor crosses lambda boundaries
/// and the provider rebinds <c>params</c> when translating the body.
/// </summary>
public sealed record IrExprLambda(IReadOnlyList<IrExprLambdaParam> Params, IrExprTreeNode Body)
    : IrExprTreeNode;

/// <summary>
/// Single parameter of an <see cref="IrExprLambda"/>. <see cref="Type"/>
/// is null when Roslyn could not resolve a parameter type (e.g.
/// inferred generics with no contextual constraint).
/// </summary>
public sealed record IrExprLambdaParam(string Name, IrTypeRef? Type = null);
