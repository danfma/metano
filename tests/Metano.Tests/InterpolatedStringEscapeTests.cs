namespace Metano.Tests;

/// <summary>
/// An interpolated string lowers to a template literal whose quasis carry the
/// runtime text of each literal segment; the printer escapes them, so control
/// characters, backslashes, backticks, a <c>${</c> sequence and the C#
/// doubled-brace escapes all survive (#254).
/// </summary>
public class InterpolatedStringEscapeTests
{
    [Test]
    public async Task RegularInterpolation_EscapesEveryTemplateMetacharacter()
    {
        var output = TranspileMethodBody("""return $"a\tb\nc\r|\\d|`|${name}|{{x}}|\u0001";""");

        await Assert.That(output).Contains("return `a\\tb\\nc\\r|\\\\d|\\`|$${name}|{x}|\\u0001`;");
    }

    [Test]
    public async Task VerbatimInterpolation_KeepsBackslashesLiteral()
    {
        var output = TranspileMethodBody("return $@\"p\\n{name}\"\"q\"\"{{}}\";");

        await Assert.That(output).Contains("return `p\\\\n${name}\"q\"{}`;");
    }

    [Test]
    public async Task RawInterpolation_KeepsSingleBracesLiteral()
    {
        var output = TranspileMethodBody("return $$\"\"\"\nraw {x} {{name}} ${y}\n\"\"\";");

        await Assert.That(output).Contains("return `raw {x} ${name} \\${y}`;");
    }

    [Test]
    public async Task HandlerConversion_UnescapesDoubledBraces()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Text;

            [Transpile]
            public class Greeter
            {
                public string Greet(string name)
                {
                    var builder = new StringBuilder();
                    builder.Append($"a{{b}} {name}");
                    return builder.ToString();
                }
            }
            """
        );

        await Assert.That(result["greeter.ts"]).Contains("`a{b} ${name}`");
    }

    [Test]
    public async Task ExceptionMessageTemplate_EscapesTemplateMetacharacters()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public class Greeter
            {
                public void Fail(string name) =>
                    throw new ArgumentException($"bad `{name}` ${{x}}", nameof(name));
            }
            """
        );

        // Printed JS: `${`bad \`${name}\` \${x}`} (Parameter 'name')`
        await Assert
            .That(result["greeter.ts"])
            .Contains("`${`bad \\`${name}\\` \\${x}`} (Parameter 'name')`");
    }

    private static string TranspileMethodBody(string body)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public class Greeter
            {
                public string Greet(string name)
                {
                    {{body}}
                }
            }
            """
        );

        return result["greeter.ts"];
    }
}
