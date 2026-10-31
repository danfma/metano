namespace Metano.Compiler.TypeScript.Parenthesization.Templates;

/// <summary>
/// The loosest operator of a template outside any bracket, which is how
/// the whole template binds when it is printed as an operand.
/// <see cref="Token"/> is the operator text for a binary operator or a
/// cast (<c>as</c>/<c>satisfies</c>), so rules that look at the operator
/// itself (TS5076, a cast's trailing type) can see it.
/// </summary>
internal readonly record struct TsTemplateTopLevelOperator(TsPrecedence Precedence, string? Token);

/// <summary>
/// Classifies how a whole template binds when it is printed as an operand:
/// the loosest operator outside any bracket wins; without one, the leading
/// token decides (prefix operator → unary, <c>new</c> → member, a single
/// bracketed group or a single token → primary).
/// </summary>
internal static class TsTemplatePrecedence
{
    public static TsTemplateTopLevelOperator Classify(IReadOnlyList<TsTemplateToken> tokens)
    {
        if (tokens.Count == 0)
            return new TsTemplateTopLevelOperator(TsPrecedence.Primary, Token: null);

        var operators = TopLevelIndices(tokens)
            .Select(i => OperatorAt(tokens, i))
            .Where(op => op.HasValue)
            .Select(op => op!.Value)
            .ToList();

        return operators.Count > 0
            ? operators.MinBy(op => op.Precedence)
            : new TsTemplateTopLevelOperator(OperatorlessPrecedence(tokens), Token: null);
    }

    /// <summary>
    /// <c>( … )</c>, <c>[ … ]</c> or <c>{ … }</c> spanning the whole text:
    /// the first bracket closes on the last token.
    /// </summary>
    public static bool IsSingleBracketedGroup(IReadOnlyList<TsTemplateToken> tokens) =>
        tokens is [{ Kind: TsTemplateTokenKind.Open }, ..] && TopLevelIndices(tokens).Count() == 1;

    private static IEnumerable<int> TopLevelIndices(IReadOnlyList<TsTemplateToken> tokens)
    {
        var depth = 0;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (depth == 0)
                yield return i;

            depth += tokens[i].DepthDelta;
        }
    }

    private static TsTemplateTopLevelOperator? OperatorAt(
        IReadOnlyList<TsTemplateToken> tokens,
        int index
    ) =>
        tokens[index] switch
        {
            { Kind: TsTemplateTokenKind.Comma } => Untokened(TsPrecedence.Lowest),
            { Kind: TsTemplateTokenKind.Punctuator, Text: "?" } => Untokened(
                TsPrecedence.Conditional
            ),
            { Kind: TsTemplateTokenKind.Punctuator, Text: "=>" or "..." } => Untokened(
                TsPrecedence.Assignment
            ),
            { Kind: TsTemplateTokenKind.Keyword, Text: "yield" } => Untokened(
                TsPrecedence.Assignment
            ),
            { Kind: TsTemplateTokenKind.Keyword, Text: "as" or "satisfies" } =>
                new TsTemplateTopLevelOperator(TsPrecedence.Relational, tokens[index].Text),
            { Text: var text } when TsTemplateOperatorPositions.IsBinaryOperatorAt(tokens, index) =>
                new TsTemplateTopLevelOperator(TsPrecedenceClassifier.OfOperator(text), text),
            _ => null,
        };

    private static TsTemplateTopLevelOperator Untokened(TsPrecedence precedence) =>
        new(precedence, Token: null);

    private static TsPrecedence OperatorlessPrecedence(IReadOnlyList<TsTemplateToken> tokens)
    {
        if (TsTemplateOperatorPositions.IsPrefixOperatorAt(tokens, 0))
            return TsPrecedence.Unary;

        if (tokens[0].Is(TsTemplateTokenKind.Keyword, "new"))
            return TsPrecedence.Member;

        if (IsAtomic(tokens))
            return TsPrecedence.Primary;

        return EndsWithPostfixUpdate(tokens) ? TsPrecedence.Postfix : TsPrecedence.Member;
    }

    private static bool IsAtomic(IReadOnlyList<TsTemplateToken> tokens) =>
        tokens.Count == 1 || IsSingleBracketedGroup(tokens);

    private static bool EndsWithPostfixUpdate(IReadOnlyList<TsTemplateToken> tokens) =>
        tokens[^1] is { Kind: TsTemplateTokenKind.Punctuator, Text: "++" or "--" };
}
