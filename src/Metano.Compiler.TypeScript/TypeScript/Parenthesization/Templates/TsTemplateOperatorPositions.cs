namespace Metano.Compiler.TypeScript.Parenthesization.Templates;

/// <summary>
/// Whether an operator token of a lexed template is used as a prefix or a
/// binary operator, which only its preceding token can tell
/// (<c>-a</c> vs <c>a - b</c>).
/// </summary>
internal static class TsTemplateOperatorPositions
{
    /// <summary>
    /// A token is in prefix position when nothing that ends an operand
    /// precedes it — that is what makes <c>-</c> a negation rather than a
    /// subtraction.
    /// </summary>
    public static bool IsInPrefixPosition(IReadOnlyList<TsTemplateToken> tokens, int index) =>
        index == 0 || !tokens[index - 1].EndsOperand;

    public static bool IsPrefixOperatorAt(IReadOnlyList<TsTemplateToken> tokens, int index) =>
        tokens[index] switch
        {
            { Kind: TsTemplateTokenKind.Keyword, Text: var word } => IsPrefixKeyword(word),
            { Kind: TsTemplateTokenKind.Punctuator, Text: var op } => IsPrefixPunctuator(op)
                && IsInPrefixPosition(tokens, index),
            _ => false,
        };

    /// <summary>
    /// The token is a binary or assignment operator used between two
    /// operands (<c>a - b</c>, <c>x instanceof Y</c>, <c>$this.length = 0</c>).
    /// </summary>
    public static bool IsBinaryOperatorAt(IReadOnlyList<TsTemplateToken> tokens, int index) =>
        tokens[index] switch
        {
            { Kind: TsTemplateTokenKind.Keyword, Text: "instanceof" or "in" } => true,
            { Kind: TsTemplateTokenKind.Punctuator, Text: var op } => IsBinaryPunctuator(op)
                && !IsInPrefixPosition(tokens, index),
            _ => false,
        };

    private static bool IsPrefixKeyword(string word) =>
        word is "typeof" or "void" or "delete" or "await";

    private static bool IsPrefixPunctuator(string op) =>
        op is "!" or "~" or "+" or "-" or "++" or "--";

    private static bool IsBinaryPunctuator(string op) =>
        TsPrecedenceClassifier.OfOperator(op) is not TsPrecedence.Lowest;
}
