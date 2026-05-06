namespace Metano.Compiler.IR;

/// <summary>
/// IR shape of a lambda body captured for IQueryable analysis. Mirrors
/// the runtime <c>ExprTree</c> union in
/// <c>metano-runtime/system/linq/expr-tree.ts</c> — the bridge
/// lowers each variant to the matching TS object literal.
///
/// <para>
/// MVP subset (Phase B / #31): param, capture, literal, member, call,
/// binary, unary, conditional. Phase B's walker emits MS0024 when the
/// lambda body uses constructs outside this list.
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
