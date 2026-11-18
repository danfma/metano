using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// What the JavaScript lexical structure of inline TypeScript (<c>Js.Code</c>) says
/// about its holes and its end: the holes that fall inside a string literal
/// (<c>'…'</c>, <c>"…"</c>, or the text of a <c>`…`</c> template outside
/// <c>${…}</c>), and whether the text ends inside a <c>//</c> line comment.
/// </summary>
internal sealed record InlineCodeScan(
    IReadOnlyList<IrInlineCodeHole> HolesInStringLiterals,
    bool EndsInLineComment
);

/// <summary>
/// A minimal JavaScript lexer over inline TypeScript: string literals with their
/// escapes, template literals with nested <c>${…}</c> substitutions, and comments.
/// A hole is an operand of the code around it. Regular-expression literals are not
/// recognized, so a quote inside one is read as a string delimiter (a documented
/// limit of <c>MS0056</c>).
/// </summary>
internal static class InlineCodeLexer
{
    public static InlineCodeScan Scan(IReadOnlyList<IrInlineCodePart> parts)
    {
        var lexer = new State();
        var holesInStrings = new List<IrInlineCodeHole>();

        foreach (var part in parts)
        {
            switch (part)
            {
                case IrInlineCodeText text:
                    lexer.Read(text.Text);
                    break;
                case IrInlineCodeHole hole:
                    if (lexer.InStringLiteral)
                        holesInStrings.Add(hole);

                    lexer.ReadOperand();
                    break;
            }
        }

        return new InlineCodeScan(holesInStrings, lexer.InLineComment);
    }

    private enum Mode
    {
        Code,
        SingleQuoted,
        DoubleQuoted,
        Template,
        LineComment,
        BlockComment,
    }

    private sealed class State
    {
        /// <summary>The open <c>{</c> count of each enclosing template substitution.</summary>
        private readonly Stack<int> _substitutionBraces = new();

        private Mode _mode = Mode.Code;
        private char _previous;
        private bool _escaped;

        public bool InStringLiteral =>
            _mode is Mode.SingleQuoted or Mode.DoubleQuoted or Mode.Template;

        public bool InLineComment => _mode is Mode.LineComment;

        public void Read(string text)
        {
            foreach (var character in text)
                Read(character);
        }

        /// <summary>A hole: no character pairs across it.</summary>
        public void ReadOperand()
        {
            _previous = '\0';
            _escaped = false;
        }

        private void Read(char character)
        {
            var previous = _previous;
            _previous = character;

            switch (_mode)
            {
                case Mode.Code:
                    ReadCode(character, previous);
                    break;
                case Mode.SingleQuoted:
                    ReadQuoted(character, '\'');
                    break;
                case Mode.DoubleQuoted:
                    ReadQuoted(character, '"');
                    break;
                case Mode.Template:
                    ReadTemplate(character, previous);
                    break;
                case Mode.LineComment:
                    if (character == '\n')
                        _mode = Mode.Code;

                    break;
                case Mode.BlockComment:
                    if (previous == '*' && character == '/')
                        Enter(Mode.Code);

                    break;
            }
        }

        private void ReadCode(char character, char previous)
        {
            switch (character)
            {
                case '/' when previous == '/':
                    Enter(Mode.LineComment);
                    break;
                case '*' when previous == '/':
                    Enter(Mode.BlockComment);
                    break;
                case '\'':
                    _mode = Mode.SingleQuoted;
                    break;
                case '"':
                    _mode = Mode.DoubleQuoted;
                    break;
                case '`':
                    _mode = Mode.Template;
                    break;
                case '{' when _substitutionBraces.Count > 0:
                    _substitutionBraces.Push(_substitutionBraces.Pop() + 1);
                    break;
                case '}' when _substitutionBraces.Count > 0:
                    CloseBrace();
                    break;
            }
        }

        private void CloseBrace()
        {
            var open = _substitutionBraces.Pop();

            if (open == 0)
                _mode = Mode.Template;
            else
                _substitutionBraces.Push(open - 1);
        }

        private void ReadQuoted(char character, char quote)
        {
            if (_escaped)
            {
                _escaped = false;
                return;
            }

            if (character == '\\')
                _escaped = true;
            else if (character == quote || character == '\n')
                _mode = Mode.Code;
        }

        private void ReadTemplate(char character, char previous)
        {
            if (_escaped)
            {
                _escaped = false;
                return;
            }

            switch (character)
            {
                case '\\':
                    _escaped = true;
                    break;
                case '`':
                    _mode = Mode.Code;
                    break;
                case '{' when previous == '$':
                    _substitutionBraces.Push(0);
                    Enter(Mode.Code);
                    break;
            }
        }

        /// <summary>Switches mode with the opening pair consumed, so it cannot close it too.</summary>
        private void Enter(Mode mode)
        {
            _mode = mode;
            _previous = '\0';
        }
    }
}
