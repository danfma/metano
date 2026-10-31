namespace Metano.Compiler.TypeScript.Parenthesization;

/// <summary>
/// A <see cref="TsSlotKind"/> plus the operator that owns the slot, when
/// the rule depends on it. Invariant (kept by the factories): binary and
/// prefix slots always carry their operator, without surrounding spaces.
/// </summary>
internal readonly record struct TsOperandSlot(TsSlotKind Kind, string? Operator = null)
{
    public static TsOperandSlot Postfix { get; } = new(TsSlotKind.Postfix);

    public static TsOperandSlot Await { get; } = new(TsSlotKind.Prefix, "await");

    public static TsOperandSlot CastOperand { get; } = new(TsSlotKind.CastOperand);

    public static TsOperandSlot AccessTarget { get; } = new(TsSlotKind.AccessTarget);

    public static TsOperandSlot MemberObject { get; } = new(TsSlotKind.MemberObject);

    public static TsOperandSlot NewCallee { get; } = new(TsSlotKind.NewCallee);

    public static TsOperandSlot ConditionalTest { get; } = new(TsSlotKind.ConditionalTest);

    public static TsOperandSlot AssignmentExpression { get; } =
        new(TsSlotKind.AssignmentExpression);

    public static TsOperandSlot ConciseArrowBody { get; } = new(TsSlotKind.ConciseArrowBody);

    public static TsOperandSlot ExpressionStatement { get; } = new(TsSlotKind.ExpressionStatement);

    /// <summary>The operator of a binary or prefix slot.</summary>
    public string RequiredOperator =>
        Operator ?? throw new InvalidOperationException($"{Kind} slot carries no operator.");

    public static TsOperandSlot BinaryLeft(string op) => new(TsSlotKind.BinaryLeft, op);

    public static TsOperandSlot BinaryRight(string op) => new(TsSlotKind.BinaryRight, op);

    /// <summary>
    /// The AST keeps keyword operators with their trailing space
    /// (<c>"typeof "</c>); the slot stores the bare token.
    /// </summary>
    public static TsOperandSlot Prefix(string op) => new(TsSlotKind.Prefix, op.Trim());
}
