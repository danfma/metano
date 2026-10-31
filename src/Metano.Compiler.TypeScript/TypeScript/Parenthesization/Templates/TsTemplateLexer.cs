namespace Metano.Compiler.TypeScript.Parenthesization.Templates;

/// <summary>
/// A small JavaScript lexer for the text of a <c>[MapMethod]</c> /
/// <c>[MapProperty]</c> <c>JsTemplate</c> or an <c>[Emit]</c> template. It
/// recognizes exactly what the parenthesization analysis needs:
/// placeholders, operands (identifiers, numbers, strings, template strings,
/// regex literals), keyword operators, punctuators and brackets. Placeholders are
/// matched by <see cref="TsTemplatePlaceholder.Match"/>, the same matcher
/// the printer substitutes with. Returns <c>null</c> for text it cannot lex (an unterminated
/// string or regex), which callers treat as "unknown shape".
/// Known limits, all of which fall back to fewer slots rather than a wrong
/// one: a template string (backtick) is one opaque operand, so neither
/// <c>${…}</c> holes nor placeholders inside it are analyzed; <c>//</c>
/// comments are not recognized; a placeholder glued to an identifier
/// (<c>foo$0</c>) is part of that identifier. A number literal is one
/// operand including its exponent sign (<c>1e+5</c>), radix prefix,
/// separators and <c>n</c> suffix.
/// </summary>
internal sealed class TsTemplateLexer
{
    // Longest first, so `>>>=` wins over `>>>`, `>>=`, `>>` and `>`.
    private static readonly string[] Punctuators =
    [
        ">>>=",
        "...",
        "===",
        "!==",
        "**=",
        "<<=",
        ">>=",
        ">>>",
        "&&=",
        "||=",
        "??=",
        "=>",
        "==",
        "!=",
        "<=",
        ">=",
        "&&",
        "||",
        "??",
        "?.",
        "++",
        "--",
        "+=",
        "-=",
        "*=",
        "/=",
        "%=",
        "&=",
        "|=",
        "^=",
        "**",
        "<<",
        ">>",
    ];

    private static readonly HashSet<string> KeywordOperators =
    [
        "typeof",
        "void",
        "delete",
        "await",
        "new",
        "instanceof",
        "in",
        "as",
        "satisfies",
        "keyof",
        "yield",
    ];

    private readonly string _text;
    private readonly List<TsTemplateToken> _tokens = [];
    private int _position;

    private TsTemplateLexer(string text) => _text = text;

    public static IReadOnlyList<TsTemplateToken>? Tokenize(string text) =>
        new TsTemplateLexer(text).TokenizeAll();

    private IReadOnlyList<TsTemplateToken>? TokenizeAll()
    {
        while (_position < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_position]))
            {
                _position++;
                continue;
            }

            if (!TryLexToken())
                return null;
        }

        return _tokens;
    }

    private bool TryLexToken()
    {
        var current = _text[_position];

        if (current == '$' && TryLexPlaceholder())
            return true;

        return current switch
        {
            '"' or '\'' or '`' => TryLexQuoted(current),
            '/' when !PreviousEndsOperand() => TryLexRegex(),
            '(' or '[' or '{' => AddToken(TsTemplateTokenKind.Open, 1),
            ')' or ']' or '}' => AddToken(TsTemplateTokenKind.Close, 1),
            ',' => AddToken(TsTemplateTokenKind.Comma, 1),
            _ when IsNumberStart() => LexNumber(),
            _ when IsIdentifierStart(current) => LexWord(),
            _ => LexPunctuator(),
        };
    }

    private bool TryLexPlaceholder()
    {
        if (TsTemplatePlaceholder.Match(_text, _position) is not { } placeholder)
            return false;

        if (!placeholder.IsValue)
            return AddToken(TsTemplateTokenKind.Operand, placeholder.Length);

        _tokens.Add(
            new TsTemplateToken(
                TsTemplateTokenKind.Placeholder,
                _text.Substring(_position, placeholder.Length),
                _position,
                placeholder
            )
        );
        _position += placeholder.Length;

        return true;
    }

    private bool TryLexQuoted(char quote)
    {
        var end = _position + 1;

        while (end < _text.Length && _text[end] != quote)
            end += _text[end] == '\\' ? 2 : 1;

        if (end >= _text.Length)
            return false;

        return AddToken(TsTemplateTokenKind.Operand, end + 1 - _position);
    }

    private bool TryLexRegex()
    {
        var end = _position + 1;
        var inClass = false;

        while (end < _text.Length && (inClass || _text[end] != '/'))
        {
            inClass = _text[end] switch
            {
                '[' => true,
                ']' => false,
                _ => inClass,
            };
            end += _text[end] == '\\' ? 2 : 1;
        }

        if (end >= _text.Length)
            return false;

        end++;

        while (end < _text.Length && char.IsAsciiLetter(_text[end]))
            end++;

        return AddToken(TsTemplateTokenKind.Operand, end - _position);
    }

    private bool LexWord()
    {
        var end = _position;

        while (end < _text.Length && IsIdentifierPart(_text[end]))
            end++;

        var word = _text[_position..end];
        var kind = KeywordOperators.Contains(word)
            ? TsTemplateTokenKind.Keyword
            : TsTemplateTokenKind.Operand;

        return AddToken(kind, end - _position);
    }

    private bool LexNumber()
    {
        var end = _position;
        var isRadixPrefixed = IsRadixPrefixed();

        while (end < _text.Length && IsNumberContinuation(end, isRadixPrefixed))
            end++;

        return AddToken(TsTemplateTokenKind.Operand, end - _position);
    }

    private bool LexPunctuator()
    {
        var match = Punctuators.FirstOrDefault(p =>
            _text.AsSpan(_position).StartsWith(p, StringComparison.Ordinal)
        );

        return AddToken(TsTemplateTokenKind.Punctuator, match?.Length ?? 1);
    }

    private bool AddToken(TsTemplateTokenKind kind, int length)
    {
        _tokens.Add(new TsTemplateToken(kind, _text.Substring(_position, length), _position));
        _position += length;

        return true;
    }

    private bool IsNumberContinuation(int index, bool isRadixPrefixed) =>
        IsNumberPart(_text[index]) || (!isRadixPrefixed && IsExponentSign(index));

    private bool PreviousEndsOperand() => _tokens.Count > 0 && _tokens[^1].EndsOperand;

    private bool IsNumberStart() =>
        char.IsAsciiDigit(_text[_position])
        || (
            _text[_position] == '.'
            && _position + 1 < _text.Length
            && char.IsAsciiDigit(_text[_position + 1])
        );

    /// <summary>
    /// Whether the number starting at the current position carries a
    /// <c>0x</c> / <c>0b</c> / <c>0o</c> prefix. In such a literal an
    /// <c>e</c> is never an exponent marker (a hex digit under <c>0x</c>,
    /// invalid under <c>0b</c> / <c>0o</c>), so <c>0x1e+5</c> stays an
    /// addition.
    /// </summary>
    private bool IsRadixPrefixed() =>
        _text[_position] == '0'
        && _position + 1 < _text.Length
        && _text[_position + 1] is 'x' or 'X' or 'b' or 'B' or 'o' or 'O';

    /// <summary>
    /// Whether the <c>+</c> / <c>-</c> at <paramref name="index"/> is the
    /// sign of a decimal exponent (<c>1e+5</c>, <c>2.5E-3</c>): it directly
    /// follows the <c>e</c> / <c>E</c> of the current number and precedes
    /// an exponent digit.
    /// </summary>
    private bool IsExponentSign(int index) =>
        _text[index] is '+' or '-'
        && index > _position
        && _text[index - 1] is 'e' or 'E'
        && index + 1 < _text.Length
        && char.IsAsciiDigit(_text[index + 1]);

    private static bool IsNumberPart(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.';

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '$';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
}
