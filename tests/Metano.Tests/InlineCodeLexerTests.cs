using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;

namespace Metano.Tests;

/// <summary>
/// The JavaScript lexer behind <c>MS0056</c> and the statement ending of inline
/// TypeScript (spec 011 FR-031): which holes fall inside a string literal, and
/// whether the text ends inside a <c>//</c> comment. <c>@</c> marks the hole.
/// </summary>
public class InlineCodeLexerTests
{
    [Test]
    [Arguments("'a @'", true)]
    [Arguments("\"a @\"", true)]
    [Arguments("`a @`", true)]
    [Arguments("'it\\'s @'", true)]
    [Arguments("`a ${ `b @` }`", true)]
    [Arguments("\"a\" + @", false)]
    [Arguments("`a ${@}`", false)]
    [Arguments("`a ${ { v: @ }.v }`", false)]
    [Arguments("{ a: @ }", false)]
    [Arguments("/* ' */ @", false)]
    [Arguments("// '\n@", false)]
    [Arguments("'a' + '@", true)]
    [Arguments("'unterminated\n@", false)]
    public async Task HoleInStringLiteral_FollowsTheJsLexicalStructure(string text, bool inString)
    {
        var scan = InlineCodeLexer.Scan(PartsOf(text));

        await Assert.That(scan.HolesInStringLiterals.Count).IsEqualTo(inString ? 1 : 0);
    }

    [Test]
    [Arguments("f() // note", true)]
    [Arguments("f() // note\ng()", false)]
    [Arguments("f() /* note */", false)]
    [Arguments("f('//')", false)]
    [Arguments("f(@) // @", true)]
    public async Task EndsInLineComment_FollowsTheJsLexicalStructure(string text, bool inComment)
    {
        var scan = InlineCodeLexer.Scan(PartsOf(text));

        await Assert.That(scan.EndsInLineComment).IsEqualTo(inComment);
    }

    private static IReadOnlyList<IrInlineCodePart> PartsOf(string text)
    {
        var chunks = text.Split('@');
        var parts = new List<IrInlineCodePart> { new IrInlineCodeText(chunks[0]) };

        foreach (var chunk in chunks.Skip(1))
        {
            parts.Add(new IrInlineCodeHole(new IrIdentifier("h"), Span: null));
            parts.Add(new IrInlineCodeText(chunk));
        }

        return parts;
    }
}
