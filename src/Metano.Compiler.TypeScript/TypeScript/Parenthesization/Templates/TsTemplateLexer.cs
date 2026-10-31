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
/// (<c>foo$0</c>) is part of that identifier.
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

        while (end < _text.Length && IsNumberPart(_text[end]))
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

    private bool PreviousEndsOperand() => _tokens.Count > 0 && _tokens[^1].EndsOperand;

    private bool IsNumberStart() =>
        char.IsAsciiDigit(_text[_position])
        || (
            _text[_position] == '.'
            && _position + 1 < _text.Length
            && char.IsAsciiDigit(_text[_position + 1])
        );

    private static bool IsNumberPart(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.';

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_' or '$';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
}
