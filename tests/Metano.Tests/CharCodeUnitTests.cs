using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Tests;

/// <summary>
/// A <c>char</c> as a UTF-16 code unit (spec 001 FR-071 to FR-073, #501): the
/// numeric reads, the stores back into a <c>char</c>, the text positions that
/// keep the one-unit string, and the lone surrogates a file cannot hold. The
/// values run against .NET in <c>tests/Conformance</c> (<c>CharCodeProbes</c>).
/// </summary>
public class CharCodeUnitTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;

        [Transpile]
        public static class Codes
        {
            static string Pick(int i) => "int";
            static string Pick(string s) => "str";

            public static int Implicit(string s) { int code = s[0]; return code + 1; }
            public static int Explicit(string s) => (int)s[0] + 1;
            public static int Plus(char c) => c + 1;
            public static int Minus(char c) => c - 'a';
            public static int Both(char a, char b) => a - b;
            public static int Unary(char c) => +c;
            public static int Max(char a, char b) => Math.Max(a, b);
            public static bool LessThanInt(char c, int n) => c < n;
            public static bool EqualsInt(char c) => c == 97;
            public static int Conditional(bool f, char c) => f ? c : 0;
            public static long Wide(char c, int i) { long r = c + i; return r; }
            public static long Shift(long l, char c) => l << c;
            public static ushort ToUShort(char c) => c;
            public static decimal ToDecimal(char c) { decimal d = c; return d; }
            public static byte ToByte(char c) => unchecked((byte)c);
            public static string Dispatch(char c) => Pick(c);
            public static int Lambda(char c) { Func<int> f = () => c; return f(); }
            public static int Loop(string s) { int t = 0; foreach (int x in s) t += x; return t; }
            public static int[] Spread(string s) { int[] a = [.. s]; return a; }
            public static int Sum(double[] d) { int t = 0; foreach (char c in d) t += c; return t; }
        }
        """;

    [Test]
    [Arguments("const code = s[0]!.charCodeAt(0);")]
    [Arguments("return s[0]!.charCodeAt(0) + 1;")]
    [Arguments("return c.charCodeAt(0) + 1;")]
    [Arguments("return c.charCodeAt(0) - 97;")]
    [Arguments("return a.charCodeAt(0) - b.charCodeAt(0);")]
    [Arguments("return c.charCodeAt(0);")]
    [Arguments("return Math.max(a.charCodeAt(0), b.charCodeAt(0));")]
    [Arguments("return c.charCodeAt(0) < n;")]
    [Arguments("return c.charCodeAt(0) === 97;")]
    [Arguments("return f ? c.charCodeAt(0) : 0;")]
    [Arguments("const r = BigInt(c.charCodeAt(0) + i);")]
    [Arguments("l << BigInt(c.charCodeAt(0) & 63)")]
    [Arguments("const d = new Decimal(c.charCodeAt(0));")]
    [Arguments("return c.charCodeAt(0) & 0xFF;")]
    [Arguments("return Codes.pickI(c.charCodeAt(0));")]
    [Arguments("const f = () => c.charCodeAt(0);")]
    [Arguments("const x = x$element.charCodeAt(0);")]
    [Arguments("[...Array.from(s.split(\"\"), (element$temp$0) => element$temp$0.charCodeAt(0))]")]
    [Arguments("t += c.charCodeAt(0);")]
    public async Task NumericPositions_ReadTheCodeUnit(string expected)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert.That(files["codes.ts"]).Contains(expected);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    private const string TextSource = """
        using System;

        [Transpile]
        public static class Texts
        {
            public static string Left(char c) => "x" + c;
            public static string Right(char c) => c + "x";
            public static string Interpolated(char c) => $"[{c}]";
            public static string Show(char c) => c.ToString();
            public static bool Same(char c) => c == 'a';
            public static bool Reversed(char c) => 'a' == c;
            public static bool Range(char c) => c >= 'a' && c <= 'z';
            public static bool Pattern(char c) => c is >= 'a' and <= 'z';
            public static string Switch(char c) { switch (c) { case 'x': return "X"; default: return "o"; } }
            public static bool Pair(char a, char b) => a < b;
            public static int Item(string s, int i) => s[i];
        }
        """;

    [Test]
    [Arguments("return \"x\" + c;")]
    [Arguments("return c + \"x\";")]
    [Arguments("return `[${c}]`;")]
    [Arguments("return c.toString();")]
    [Arguments("return c === \"a\";")]
    [Arguments("return \"a\" === c;")]
    [Arguments("return c >= \"a\" && c <= \"z\";")]
    [Arguments("case \"x\":")]
    [Arguments("return a < b;")]
    public async Task TextPositions_KeepTheOneUnitString(string expected)
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(TextSource);

        await Assert.That(files["texts.ts"]).Contains(expected);
    }

    [Test]
    public async Task CharPattern_StaysAStringTest()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(TextSource);

        await Assert.That(files["texts.ts"]).DoesNotContain("charCodeAt(0) >= 97");
        await Assert.That(files["texts.ts"]).Contains("c >= \"a\" && c <= \"z\"");
    }

    [Test]
    [Arguments("return i === 97 ? 1 : i >= 98 && i <= 122 ? 2 : 0;")]
    [Arguments("case 98:")]
    [Arguments("return i === 97 || i >= 120;")]
    [Arguments("return i === 97;")]
    public async Task IntegerTestsAgainstCharConstants_FoldToTheCode(string expected)
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Folds
            {
                public static int Expr(int i) => i switch { 'a' => 1, >= 'b' and <= 'z' => 2, _ => 0 };
                public static int Stmt(int i) { switch (i) { case 'b': return 1; default: return 0; } }
                public static bool Is(int i) => i is 'a' or >= 'x';
                public static bool Eq(int i) => i == 'a';
            }
            """
        );

        await Assert.That(files["folds.ts"]).Contains(expected);
    }

    [Test]
    [Arguments("return String.fromCharCode((c.charCodeAt(0) + 1) & 0xFFFF);")]
    [Arguments("return String.fromCharCode(n & 0xFFFF);")]
    [Arguments("return String.fromCharCode(checkedNarrowNumber(n, 16, false));")]
    [Arguments("c = String.fromCharCode((c.charCodeAt(0) + 1) & 0xFFFF);")]
    [Arguments("c = String.fromCharCode((c.charCodeAt(0) - 1) & 0xFFFF);")]
    [Arguments("c = String.fromCharCode((c.charCodeAt(0) + 2) & 0xFFFF);")]
    [Arguments("c = String.fromCharCode(checkedNarrowNumber(c.charCodeAt(0) + 1, 16, false));")]
    [Arguments("c = String.fromCharCode(intDiv(c.charCodeAt(0), 2) & 0xFFFF);")]
    [Arguments("receiver$temp$0.charCodeAt(0) + 1")]
    [Arguments("a[i] = String.fromCharCode((a[i]!.charCodeAt(0) + 1) & 0xFFFF);")]
    public async Task Stores_GoThroughFromCharCode(string expected)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Stores
            {
                public static char Next(char c) => (char)(c + 1);
                public static char FromInt(int n) => (char)n;
                public static char FromChecked(int n) => checked((char)n);
                public static char Inc(char c) { c++; return c; }
                public static char Dec(char c) { --c; return c; }
                public static char Compound(char c) { c += (char)2; return c; }
                public static char CheckedInc(char c) { checked { c++; } return c; }
                public static char Divide(char c) { c /= (char)2; return c; }
                public static char Postfix(char c) { var old = c++; return old; }
                public static char Element(char[] a, int i) { a[i]++; return a[i]; }
            }
            """
        );

        await Assert.That(files["stores.ts"]).Contains(expected);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task NullableChar_GoesThroughTheLiftedConversion()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Lifted
            {
                public static int? Cast(char? n) => (int?)n;
                public static bool Less(char? n) => n < 5;
                public static bool Pair(char? a, char? b) => a <= b;
            }
            """
        );

        await Assert
            .That(files["lifted.ts"])
            .Contains("return n == null ? null : n.charCodeAt(0);");

        await Assert
            .That(files["lifted.ts"])
            .Contains("(a == null ? null : a.charCodeAt(0)) != null");

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("return \"a\\ud83db\";")]
    [Arguments("return \"\" + \"\\ud83d\";")]
    [Arguments("return \"\\ude00\ud83d\ude00\";")]
    [Arguments("return \"\ud83d\ude00\";")]
    [Arguments("return `x${\"\\ude00\"}y\\ud83d`;")]
    public async Task LoneSurrogates_PrintAsEscapes_AndPairsStayCharacters(string expected)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Surrogates
            {
                public static string InString() => "a\uD83Db";
                public static string Letter() => "" + '\uD83D';
                public static string Mixed() => "\uDE00\uD83D\uDE00";
                public static string Pair() => "\uD83D\uDE00";
                public static string Interpolated() => $"x{'\uDE00'}y\uD83D";
            }
            """
        );

        await Assert.That(files["surrogates.ts"]).Contains(expected);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task Dart_KeepsItsCharShape_AndEscapesALoneSurrogate()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            [Transpile]
            public static class Darts
            {
                public static int Literal() { int i = 'a'; return i; }
                public static double Double() { double d = 'a'; return d; }
                public static bool Same(char c) => c == 'a';
                public static string Lone() => "a\uD83Db";
            }
            """
        );

        await Assert.That(files["darts.dart"]).Contains("int i = 'a';");
        await Assert.That(files["darts.dart"]).Contains("double d = 97;");
        await Assert.That(files["darts.dart"]).Contains("c == 'a'");
        await Assert.That(files["darts.dart"]).Contains("'a\\uD83Db'");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task Jsx_PrintsALoneSurrogateAsAStringExpression()
    {
        var element = new TsJsxElement(
            "p",
            [new TsJsxAttribute("title", new TsJsxAttributeStringValue("a\uD83Db"))],
            [new TsJsxText("x\uD83Dy & \uD83D\uDE00")]
        );

        var output = new Printer().Print(
            new TsSourceFile(
                "view.tsx",
                [new TsFunction("view", [], null, [new TsReturnStatement(element)])]
            )
        );

        await Assert.That(output).Contains("title={\"a\\ud83db\"}");
        await Assert.That(output).Contains("x{\"\\ud83d\"}y &amp; \ud83d\ude00");
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
