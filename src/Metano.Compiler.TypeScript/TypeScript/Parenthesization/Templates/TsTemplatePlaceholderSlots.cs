namespace Metano.Compiler.TypeScript.Parenthesization.Templates;

/// <summary>
/// Derives the operand slots of one placeholder occurrence from its nearest
/// neighbor token on each side. The substituted node is wrapped when it
/// would regroup under either neighbor. A delimiter (<c>(</c>, <c>,</c>,
/// <c>)</c>, <c>]</c>, start/end of text) imposes no slot; a following
/// <c>.</c>/<c>[</c> makes the placeholder a member object, a following
/// <c>(</c>/<c>?.</c> a callee or optional receiver; a neighbor the
/// analysis does not recognize is treated as an access target, the most
/// demanding slot short of wrapping everything.
/// </summary>
internal static class TsTemplatePlaceholderSlots
{
    public static IReadOnlyList<TsOperandSlot> For(
        IReadOnlyList<TsTemplateToken> tokens,
        int placeholderIndex
    )
    {
        var slots = new List<TsOperandSlot>(capacity: 2);

        if (LeftSlot(tokens, placeholderIndex) is { } left)
            slots.Add(left);

        if (RightSlot(tokens, placeholderIndex) is { } right)
            slots.Add(right);

        return slots;
    }

    private static TsOperandSlot? LeftSlot(IReadOnlyList<TsTemplateToken> tokens, int index)
    {
        if (index == 0)
            return null;

        var previous = index - 1;

        return tokens[previous] switch
        {
            { Kind: TsTemplateTokenKind.Open or TsTemplateTokenKind.Comma } => null,
            { Kind: TsTemplateTokenKind.Punctuator, Text: ";" } => null,
            { Kind: TsTemplateTokenKind.Punctuator, Text: ":" or "?" or "..." } =>
                TsOperandSlot.AssignmentExpression,
            { Kind: TsTemplateTokenKind.Punctuator, Text: "=>" } => TsOperandSlot.ConciseArrowBody,
            { Kind: TsTemplateTokenKind.Keyword, Text: "new" } => TsOperandSlot.NewCallee,
            { Kind: TsTemplateTokenKind.Keyword, Text: "yield" } =>
                TsOperandSlot.AssignmentExpression,
            { Text: var op }
                when TsTemplateOperatorPositions.IsPrefixOperatorAt(tokens, previous) =>
                TsOperandSlot.Prefix(op),
            { Text: var op }
                when TsTemplateOperatorPositions.IsBinaryOperatorAt(tokens, previous) =>
                TsOperandSlot.BinaryRight(op),
            _ => TsOperandSlot.AccessTarget,
        };
    }

    private static TsOperandSlot? RightSlot(IReadOnlyList<TsTemplateToken> tokens, int index)
    {
        if (index == tokens.Count - 1)
            return null;

        var next = index + 1;

        return tokens[next] switch
        {
            { Kind: TsTemplateTokenKind.Close or TsTemplateTokenKind.Comma } => null,
            { Kind: TsTemplateTokenKind.Punctuator, Text: ";" or ":" } => null,
            { Kind: TsTemplateTokenKind.Punctuator, Text: "." } => TsOperandSlot.MemberObject,
            { Kind: TsTemplateTokenKind.Open, Text: "[" } => TsOperandSlot.MemberObject,
            { Kind: TsTemplateTokenKind.Punctuator, Text: "?" } => TsOperandSlot.ConditionalTest,
            { Kind: TsTemplateTokenKind.Punctuator, Text: "++" or "--" } => TsOperandSlot.Postfix,
            { Kind: TsTemplateTokenKind.Keyword, Text: "as" or "satisfies" } =>
                TsOperandSlot.CastOperand,
            { Text: var op } when TsTemplateOperatorPositions.IsBinaryOperatorAt(tokens, next) =>
                TsOperandSlot.BinaryLeft(op),
            _ => TsOperandSlot.AccessTarget,
        };
    }
}
