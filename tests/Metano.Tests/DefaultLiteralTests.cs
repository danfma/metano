using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A target-typed <c>default</c> literal and <c>default(T)</c> resolve to the
/// default of the type the compiler converts them to, wherever they appear
/// (#627, #372, #612): never the <c>undefined</c> the opaque marker prints.
/// </summary>
public class DefaultLiteralTests
{
    private const string Types = """
        using System.Collections.Generic;
        using System.Linq;

        [Transpile]
        public enum Level { Low = 1, High = 2 }

        [Transpile]
        public enum Mode { Off, On }

        [Transpile]
        public struct Point { public int X; }

        [Transpile]
        public struct Sized { public int W; public Sized(int w) { W = w; } }

        [Transpile, StringEnum]
        public enum Tone { Soft = 1, Loud = 2 }
        """;

    [Test]
    [Arguments("bool b = default; return b;", "const b = false;")]
    [Arguments("int x = default; return x;", "const x = 0;")]
    [Arguments("long l = default; return l;", "const l = 0n;")]
    [Arguments("decimal m = default; return m;", "const m = new Decimal(\"0\");")]
    [Arguments("char c = default; return c;", "const c = \"\\u0000\";")]
    [Arguments("Mode m = default; return m;", "const m = Mode.Off;")]
    [Arguments("Level v = default; return v;", "const v = 0 as Level;")]
    [Arguments("string? s = default; return s;", "const s = null as string | null;")]
    [Arguments("int? n = default; return n;", "const n = null as number | null;")]
    [Arguments("var x = default(int); return x;", "const x = 0;")]
    [Arguments("Point p = default; return p;", "const p = new Point();")]
    [Arguments("var p = default(Point); return p;", "const p = new Point();")]
    [Arguments("var b = default(bool); return b;", "const b = false;")]
    public async Task LocalInitializer_TakesTheDefaultOfItsType(string body, string lowered)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + $$"""

                [Transpile]
                public class Holder
                {
                    public object? Run() { {{body}} }
                }
                """
        );

        await Assert.That(files["holder.ts"]).Contains(lowered);
        await Assert.That(Warnings(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task NestedPositions_TakeTheDefaultOfTheirConvertedType()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + """

                [Transpile]
                public class Holder
                {
                    public int Conditional(List<int> xs, bool cond) => xs.FirstOrDefault(cond ? 9 : default);

                    public int Coalesced(List<int> xs) => xs.FirstOrDefault(defaultValue: (int?)null ?? default);

                    public int FromLocal(List<int> xs)
                    {
                        int d = default;

                        return xs.FirstOrDefault(d);
                    }

                    public int Coalesce(int? n) => n ?? default;

                    public List<int> Elements() => [default, 1];

                    public Mode Switched(bool cond) => cond ? Mode.On : default;
                }
                """
        );

        var holder = files["holder.ts"];

        await Assert.That(holder).Contains("firstOrDefault(undefined, cond ? 9 : 0)");
        await Assert.That(holder).Contains("firstOrDefault(undefined, null ?? 0)");
        await Assert.That(holder).Contains("const d = 0;");
        await Assert.That(holder).Contains("return n ?? 0;");
        await Assert.That(holder).Contains("return [0, 1];");
        await Assert.That(holder).Contains("return cond ? Mode.On : Mode.Off;");
        await Assert.That(holder).DoesNotContain("undefined, undefined");
        await Assert.That(Warnings(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A type no expression spells (a struct that declares a constructor, a
    /// tuple, a <c>[StringEnum]</c> without a zero member) reports
    /// <c>MS0001</c> and keeps the placeholder; a struct that declares none is
    /// <c>new Point()</c>, as a parameter default is.
    /// </summary>
    [Test]
    [Arguments("Sized s = default; return s;", "Sized")]
    [Arguments("var s = default(Sized); return s;", "Sized")]
    [Arguments("(int, int) t = default; return t;", "(int, int)")]
    [Arguments("Tone t = default; return t;", "Tone")]
    public async Task TypeWithoutALiteralDefault_ReportsAndKeepsThePlaceholder(
        string body,
        string type
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + $$"""

                [Transpile]
                public class Holder
                {
                    public object? Run() { {{body}} }
                }
                """
        );

        await Assert
            .That(Warnings(diagnostics))
            .Contains(message => message.Contains($"'default({type})' has no literal"));
    }

    /// <summary>
    /// The value a lookup returns when no element qualifies and the
    /// <c>GetValueOrDefault</c> fallbacks take the same default a
    /// <c>default</c> literal does: a struct that declares no constructor is
    /// <c>new Point()</c>, the zeroed value C# returns.
    /// </summary>
    [Test]
    public async Task LookupMisses_TakeTheStructDefault()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + """

                [Transpile]
                public class Holder
                {
                    public int First(List<Point> xs) => xs.FirstOrDefault().X;
                    public int Find(List<Point> xs) => xs.Find(p => p.X > 0).X;
                    public int Value(Point? p) => p.GetValueOrDefault().X;
                    public int Lookup(Dictionary<string, Point> d) => d.GetValueOrDefault("k").X;
                }
                """
        );

        var holder = files["holder.ts"];

        await Assert.That(holder).Contains("linq(xs, firstOrDefault(undefined, new Point())).x");
        await Assert.That(holder).Contains("(xs.find((p: Point) => p.x > 0) ?? new Point()).x");
        await Assert.That(holder).Contains("(p ?? new Point()).x");
        await Assert.That(holder).Contains("getValueOrDefault(d, \"k\", new Point()).x");
        await Assert.That(Warnings(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// <c>new S()</c> stands for <c>default(S)</c> with the struct's type
    /// arguments; a field whose type has no default (a type parameter, a
    /// tuple) whose type in the constructed struct is a value type is left
    /// undefined, so the literal still reports MS0001; one whose constructed
    /// type is a reference type (<c>ZBoxed&lt;string?&gt;</c>) has its <c>null</c>.
    /// </summary>
    [Test]
    public async Task StructDefaultWithAnUnsetField_KeepsItsTypeArgumentsAndReports()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + """

                [Transpile]
                public struct ZGen<T> { public T Value; public int N; }

                [Transpile]
                public struct ZOdd { public System.Guid Id; public char C; public int N; }

                [Transpile]
                public struct ZBoxed<T> { public T Value; }

                [Transpile]
                public class Holder
                {
                    public int Gen() { ZGen<int> a = default; return a.N; }
                    public int Odd() { ZOdd z = default; return z.N; }
                    public string? Boxed() { ZBoxed<string?> b = default; return b.Value; }
                }
                """
        );

        var holder = files["holder.ts"];

        await Assert.That(holder).Contains("const a = new ZGen<number>();");
        await Assert.That(holder).Contains("const z = new ZOdd();");
        await Assert.That(files["z-odd.ts"]).Contains("id: UUID = UUID.empty;");
        await Assert.That(files["z-odd.ts"]).Contains("c: string = \"\\u0000\";");

        await Assert
            .That(Warnings(diagnostics))
            .IsEquivalentTo(["'default(ZGen<int>)' has no target default for field 'Value'."]);
    }

    private static IEnumerable<string> Warnings(IEnumerable<MetanoDiagnostic> diagnostics) =>
        diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).Select(d => d.Message);
}
