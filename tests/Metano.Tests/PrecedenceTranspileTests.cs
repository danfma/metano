namespace Metano.Tests;

/// <summary>
/// The IR drops C# source parentheses, so the TypeScript printer rebuilds
/// grouping from tree shape and JS precedence (#244, FR-014). Each row is a
/// C# member on a probe class and the TS line it must lower to. Every
/// expected line was type-checked with tsgo and run with bun against the C#
/// result when the case table was drawn up for #244; the case IDs only label
/// rows (C: expressions, P: patterns and printer-only shapes, E: [Emit] and
/// BCL templates).
/// Parentheses are grammar-minimal — a pair is added only where printing
/// bare would regroup the tree or be rejected (TS5076, token gluing,
/// <c>1.toString()</c>).
/// </summary>
public class PrecedenceTranspileTests
{
    private const string Prelude = """
        using System.Collections.Generic;
        using System.Linq;

        [Transpile]
        public class Box
        {
            public Box(int v) { V = v; }

            public int V { get; set; }

            public int Get() => V;
        }

        [Transpile, PlainObject]
        public record Pt(int X);

        [Transpile]
        public record Rec(int X);

        [Transpile]
        public class Holder
        {
            public bool Flag { get; set; }

            public Holder Next { get; set; } = null!;
        }

        [Transpile, System.Flags]
        public enum F { None = 0, A = 1, B = 2 }

        """;

    [Test]
    // Access targets: a low-precedence receiver keeps its grouping.
    [Arguments(
        "C01",
        "string M(string? a, string b) => (a ?? b).Trim();",
        "return (a ?? b).trim();"
    )]
    [Arguments("C02", "int M(string? a, string b) => (a ?? b).Length;", "return (a ?? b).length;")]
    [Arguments(
        "C03",
        "string M(bool c, string a, string b) => (c ? a : b).ToUpper();",
        "return (c ? a : b).toUpperCase();"
    )]
    [Arguments(
        "C12",
        "async Task<string> M(Task<string> t) => (await t).ToUpper();",
        "return (await t).toUpperCase();"
    )]
    [Arguments("C15", "int M(Box t, Box b) => (t = b).Get() + t.V;", "return (t = b).get() + t.v;")]
    [Arguments("C55", "string M(int i) => (++i).ToString();", "return (++i).toString();")]
    [Arguments("C64", "char M(string? a, string b) => (a ?? b)[0];", "return (a ?? b)[0];")]
    [Arguments(
        "C67",
        "string M(int a, int b) => (a > b ? a : b).ToString();",
        "return (a > b ? a : b).toString();"
    )]
    [Arguments("P3", "int? M(string? a, string b) => (a ?? b)?.Length;", "return (a ?? b)?.length")]
    [Arguments(
        "P4",
        "Rec M(bool f, Rec a, Rec b) => (f ? a : b) with { X = 1 };",
        "(f ? a : b).with("
    )]
    [Arguments("P5", "string M() => 1.ToString();", "return (1).toString();")]
    [Arguments("P8", "bool M(Holder? h) => (h?.Next).Flag;", "return (h?.next).flag;")]
    [Arguments(
        "Consumer.Trim",
        "string M(string? value) => (value ?? \"\").Trim();",
        "return (value ?? \"\").trim();"
    )]
    // Arithmetic and bitwise operands.
    [Arguments("C04", "int M(int a, int b, int c) => (a + b) * c;", "return (a + b) * c;")]
    [Arguments("C05", "int M(int a, int b, int c) => a - (b - c);", "return a - (b - c);")]
    [Arguments(
        "C06",
        "double M(double a, double b, double c) => a / (b * c);",
        "return a / (b * c);"
    )]
    [Arguments("C07", "int M(int a, int b, int c) => a % (b + c);", "return a % (b + c);")]
    [Arguments("C24", "string M(int x, int y) => \"s\" + (x + y);", "return \"s\" + (x + y);")]
    [Arguments("C25", "string M(int x, int y) => (\"s\" + x) + y;", "return \"s\" + x + y;")]
    [Arguments(
        "C26",
        "bool M(int a, int b, int c, int d) => (a < b) == (c < d);",
        "return a < b === c < d;"
    )]
    [Arguments("C47", "bool M(int a, int b, int c) => (a & b) == c;", "return (a & b) === c;")]
    [Arguments("C48", "int M(int a, int b, int c) => a | b ^ c;", "return a | b ^ c;")]
    [Arguments("C49", "int M(int a, int b, int c) => (a | b) ^ c;", "return (a | b) ^ c;")]
    [Arguments("C50", "int M(int a, int b, int c) => a << (b + c);", "return a << b + c;")]
    [Arguments("C51", "int M(int a, int b, int c) => (a << b) + c;", "return (a << b) + c;")]
    [Arguments("C52", "int M(int a, int b, int c) => (a + b) >> c;", "return a + b >> c;")]
    [Arguments("C53", "int M(int t, int b) => (t += b) * 2;", "return (t += b) * 2;")]
    [Arguments(
        "C54",
        "int M(Box[] boxes, int i) => boxes[i++].Get() * 10 + i;",
        "return boxes[i++].get() * 10 + i;"
    )]
    [Arguments("C63", "int M() { int x; return (x = 5) + 1 + x; }", "return (x = 5) + 1 + x;")]
    [Arguments(
        "C72",
        "int M(int a, int b) => (a - b) - (a - b) * 2;",
        "return a - b - (a - b) * 2;"
    )]
    // Prefix operators, including token gluing.
    [Arguments("C08", "int M(int a, int b) => -(a + b);", "return -(a + b);")]
    [Arguments("C09", "int M(int a) => -(-a);", "return -(-a);")]
    [Arguments("C10", "bool M(bool a, bool b) => !(a && b);", "return !(a && b);")]
    [Arguments("C56", "int M(int a, int b) => a - -b;", "return a - -b;")]
    [Arguments("C57", "int M(int a, int b) => a + +b - --b;", "return a + b - --b;")]
    [Arguments(
        "C65",
        "async Task<string> M(Task<string> t) => (await t) + \"x\";",
        "return await t + \"x\";"
    )]
    [Arguments("C66", "int M(int a, int b, int c) => -(a - b) * c;", "return -(a - b) * c;")]
    [Arguments("C68", "bool M(int a, int b) => !(a == b);", "return !(a === b);")]
    [Arguments("P6", "int M(int b) => -(--b);", "return -(--b);")]
    [Arguments(
        "Consumer.NotNaN",
        "bool M(double value) => !(value * 0 == 0);",
        "return !(value * 0 === 0);"
    )]
    // `??` chains and the TS5076 `??` / `||` / `&&` mixing rule.
    [Arguments(
        "C16",
        "string M(string? a, string? b, string c) => a ?? (b ?? c);",
        "return a ?? b ?? c;"
    )]
    [Arguments(
        "C17",
        "string M(string? a, string? b, string c) => (a ?? b) ?? c;",
        "return a ?? b ?? c;"
    )]
    [Arguments("C18", "bool M(bool? a, bool b, bool c) => (a ?? b) || c;", "return (a ?? b) || c;")]
    [Arguments("C19", "bool M(bool? a, bool b, bool c) => a ?? (b || c);", "return a ?? (b || c);")]
    [Arguments("C20", "bool M(bool? a, bool b, bool c) => a ?? b || c;", "return a ?? (b || c);")]
    [Arguments(
        "C21",
        "bool M(bool a, bool b, bool c) => ((bool?)(a || b)) ?? c;",
        "return (a || b) ?? c;"
    )]
    [Arguments("C22", "bool M(bool a, bool b, bool c) => (a && b) || c;", "return a && b || c;")]
    [Arguments("C23", "bool M(bool a, bool b, bool c) => a && (b || c);", "return a && (b || c);")]
    [Arguments(
        "C69",
        "string M(string? a, string b, string c) => (a ?? b) + c;",
        "return (a ?? b) + c;"
    )]
    [Arguments(
        "C70",
        "string M(string? a, string b, string c) => a ?? b + c;",
        "return a ?? b + c;"
    )]
    [Arguments("P7", "bool M(bool? x, bool p, bool q) => x ?? (p || q);", "return x ?? (p || q);")]
    // Conditional slots (grammar-minimal: C30 and C71 stay bare).
    [Arguments(
        "C29",
        "int M(bool a, bool b, bool c) => (a ? b : c) ? 1 : 2;",
        "return (a ? b : c) ? 1 : 2;"
    )]
    [Arguments(
        "C30",
        "int M(bool a, bool b, bool c) => a ? (b ? 1 : 2) : (c ? 3 : 4);",
        "return a ? b ? 1 : 2 : c ? 3 : 4;"
    )]
    [Arguments("C31", "int M(bool a, bool b) => a ? 1 : b ? 2 : 3;", "return a ? 1 : b ? 2 : 3;")]
    [Arguments(
        "C46",
        "int M(List<int> list) => list.Count > 0 ? list[0] : -1;",
        "return list.length > 0 ? list[0] : -1;"
    )]
    [Arguments("C62", "int M(bool c, int a, int b) => (c ? a : b) + 1;", "return (c ? a : b) + 1;")]
    [Arguments(
        "C71",
        "int M(Holder? h, bool b) => (h?.Flag ?? b) ? 1 : 0;",
        "return h?.flag ?? b ? 1 : 0;"
    )]
    // Patterns and switch subjects.
    [Arguments(
        "C27",
        "bool M(int x, bool f) => f && x is > 0 and < 10;",
        "return f && x > 0 && x < 10;"
    )]
    [Arguments(
        "C28",
        "string M(int? x) => (x ?? 0) switch { 0 => \"zero\", 1 => \"one\", _ => \"many\" };",
        "return (x ?? 0) === 0 ? \"zero\" : (x ?? 0) === 1 ? \"one\" : \"many\";"
    )]
    [Arguments(
        "P1",
        "bool M(int n) => n is (1 or 2) and > 0;",
        "return (n === 1 || n === 2) && n > 0;"
    )]
    [Arguments("P2", "bool M(int? x) => (x ?? 0) is 0;", "return (x ?? 0) === 0;")]
    // Arrows, calls and `new`.
    [Arguments(
        "C33",
        "int M() { System.Func<Pt> f = () => new Pt(7); return f().X; }",
        "const f = () => ({ x: 7 });"
    )]
    [Arguments("C34", "int M() => ((System.Func<int>)(() => 1))();", "return (() => 1)();")]
    [Arguments("C35", "int M() => (new Box(3)).Get();", "return new Box(3).get();")]
    [Arguments("C36", "int M() => new Box(4).Get();", "return new Box(4).get();")]
    // Interpolation holes accept any expression; only the inner `+` needs grouping.
    [Arguments(
        "C37",
        "string M(bool c, string? s) => $\"a{(c ? \"y\" : \"n\")}b{s ?? \"-\"}c{(c ? 1 : 2) + 1}\";",
        "return `a${c ? \"y\" : \"n\"}b${s ?? \"-\"}c${(c ? 1 : 2) + 1}`;"
    )]
    // BCL templates: placeholders are wrapped by their position in the template.
    [Arguments(
        "C40",
        "double M(int a, int b) => Math.Pow(a + b, 2) + Math.Pow(-a, 2);",
        "return Math.pow(a + b, 2) + Math.pow(-a, 2);"
    )]
    [Arguments(
        "C41",
        "bool M(F f, F g) => (f | g).HasFlag(F.B);",
        "return ((f | g) & F.B) === F.B;"
    )]
    [Arguments(
        "C42",
        "bool M(F x) => x.HasFlag(F.A | F.B);",
        "return (x & (F.A | F.B)) === (F.A | F.B);"
    )]
    [Arguments(
        "C43",
        "int M(Stack<int>? s1, Stack<int> s2) => (s1 ?? s2).Peek();",
        "return (s1 ?? s2)[(s1 ?? s2).length - 1];"
    )]
    [Arguments(
        "C44",
        "int M(bool c) => Enumerable.Range(c ? 1 : 2, 3).ToList()[2];",
        "(_, i) => (c ? 1 : 2) + i"
    )]
    [Arguments(
        "C45",
        "int M(Guid? g) => (g ?? Guid.Empty).ToString().Length;",
        "return (g ?? UUID.empty).length;"
    )]
    [Arguments(
        "E4",
        "void M(bool f, List<int> l1, List<int> l2) => (f ? l1 : l2).Clear();",
        "(f ? l1 : l2).length = 0"
    )]
    [Arguments("E5", "void M(int a, int b) => Console.WriteLine(a + b);", "console.log(a + b)")]
    public async Task Expression_LowersWithGrammarMinimalParentheses(
        string caseId,
        string member,
        string expectedLine
    )
    {
        var output = TranspileProbe($"public static {member}");

        await Assert.That(output).Contains(expectedLine).Because(caseId);
    }

    [Test]
    // [Emit] templates: the template text decides both how the whole call
    // binds as an operand and which arguments need grouping.
    [Arguments("C38", "Up(a ?? b)", "return (a ?? b).toUpperCase();")]
    [Arguments("C39", "Dbl(x + y)", "return (x + y) * 2;")]
    [Arguments("E1", "Add(x, y) * 2", "return (x + y) * 2;")]
    [Arguments("E2", "Add(x, y).ToString()", "return (x + y).toString();")]
    [Arguments("E3", "Show(Add(x, y))", "show(x + y)")]
    [Arguments("E6", "-Neg(x)", "return -(-x);")]
    [Arguments("E7", "Kind(a ?? b) + \"!\"", "return typeof (a ?? b) + \"!\";")]
    public async Task EmitTemplate_ParenthesizesByPlaceholderPosition(
        string caseId,
        string call,
        string expectedLine
    )
    {
        var output = TranspileProbe(
            $$"""
            [Emit("$0.toUpperCase()")] public static string Up(string s) => s;
            [Emit("$0 * 2")] public static int Dbl(int v) => v;
            [Emit("$0 + $1")] public static int Add(int l, int r) => l;
            [Emit("-$0")] public static int Neg(int v) => v;
            [Emit("typeof $0")] public static string Kind(object? v) => "";
            public static string Show(int v) => "";
            public static object M(int x, int y, string? a, string b) => {{call}};
            """
        );

        await Assert.That(output).Contains(expectedLine).Because(caseId);
    }

    private static string TranspileProbe(string members)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            {{Prelude}}
            [Transpile]
            public class Probe
            {
                {{members}}
            }
            """
        );

        return result["probe.ts"];
    }
}
