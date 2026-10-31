using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Parenthesization.Templates;

internal enum TsTemplatePlaceholderKind
{
    /// <summary><c>$this</c>: the template's receiver.</summary>
    Receiver,

    /// <summary><c>$N</c>: the N-th template argument.</summary>
    Argument,

    /// <summary><c>$TN</c>: the N-th generic type-argument name.</summary>
    TypeArgument,
}

/// <summary>
/// One placeholder occurrence in a <see cref="TsTemplate"/> text. This is
/// the single parser of the placeholder syntax: the printer substitutes the
/// occurrences it matches and the lexer turns the same matches into tokens,
/// so both agree on where a placeholder starts and ends. One exception: a
/// placeholder glued to an identifier (<c>foo$0</c>) is lexed as part of
/// that identifier, so it is substituted but gets no slots (printed bare).
/// </summary>
internal readonly record struct TsTemplatePlaceholder(
    TsTemplatePlaceholderKind Kind,
    int Index,
    int Length
)
{
    private const string ReceiverText = "$this";

    public bool IsValue => Kind is not TsTemplatePlaceholderKind.TypeArgument;

    /// <summary>
    /// Matches the placeholder starting at <paramref name="offset"/> (which
    /// must point at a <c>$</c>), or returns <c>null</c> for a lone or
    /// unrecognized <c>$</c>.
    /// </summary>
    public static TsTemplatePlaceholder? Match(string text, int offset)
    {
        var rest = text.AsSpan(offset);

        if (rest.StartsWith(ReceiverText, StringComparison.Ordinal))
            return new TsTemplatePlaceholder(
                TsTemplatePlaceholderKind.Receiver,
                Index: 0,
                ReceiverText.Length
            );

        if (rest is ['$', 'T', var digit, ..] && char.IsAsciiDigit(digit))
            return Numbered(TsTemplatePlaceholderKind.TypeArgument, rest, prefixLength: 2);

        if (rest is ['$', var first, ..] && char.IsAsciiDigit(first))
            return Numbered(TsTemplatePlaceholderKind.Argument, rest, prefixLength: 1);

        return null;
    }

    /// <summary>
    /// The indices of the <c>$N</c> argument placeholders that occur in
    /// <paramref name="text"/>, so a caller can tell which arguments a
    /// template drops.
    /// </summary>
    public static IReadOnlySet<int> ReferencedArgumentIndices(string text)
    {
        var referenced = new HashSet<int>();

        for (var dollar = text.IndexOf('$'); dollar >= 0; dollar = text.IndexOf('$', dollar + 1))
        {
            if (Match(text, dollar) is { Kind: TsTemplatePlaceholderKind.Argument } argument)
                referenced.Add(argument.Index);
        }

        return referenced;
    }

    /// <summary>
    /// The node the printer substitutes for a value placeholder, or
    /// <c>null</c> when the template does not supply one (the printer then
    /// writes nothing for <c>$this</c> and the raw text for <c>$N</c>).
    /// </summary>
    public TsExpression? Resolve(TsTemplate template) =>
        Kind switch
        {
            TsTemplatePlaceholderKind.Receiver => template.Receiver,
            TsTemplatePlaceholderKind.Argument when Index < template.Arguments.Count =>
                template.Arguments[Index],
            _ => null,
        };

    private static TsTemplatePlaceholder? Numbered(
        TsTemplatePlaceholderKind kind,
        ReadOnlySpan<char> rest,
        int prefixLength
    )
    {
        var digits = rest[prefixLength..];
        var digitCount = digits.IndexOfAnyExceptInRange('0', '9');
        var length = digitCount < 0 ? digits.Length : digitCount;

        return int.TryParse(digits[..length], out var index)
            ? new TsTemplatePlaceholder(kind, index, prefixLength + length)
            : null;
    }
}
