namespace Metano.Tests;

/// <summary>
/// A record field whose <c>===</c> is not .NET's
/// <c>EqualityComparer&lt;T&gt;.Default</c> compares through the runtime's
/// <c>valueEquals</c> (#674): a <c>double</c> / <c>float</c>, whose <c>NaN</c>
/// equals itself on .NET. A field whose static type does not say how it
/// compares (a type parameter, <c>object</c>, an interface, a class) goes
/// through <c>defaultEquals</c>, .NET's <c>EqualityComparer&lt;T&gt;.Default</c>:
/// the value's own <c>equals</c>, else reference equality.
/// </summary>
public class RecordFieldEqualityTests
{
    [Test]
    [Arguments("double")]
    [Arguments("float")]
    [Arguments("double?")]
    public async Task FloatingPointField_ComparesThroughValueEquals(string type)
    {
        var files = TranspileHelper.Transpile(
            $"""
            [Transpile]
            public record Reading({type} X);
            """
        );

        var reading = files["reading.ts"];

        await Assert.That(reading).Contains("valueEquals(this.x, other.x)");
        await Assert.That(reading).Contains("import { HashCode, copyRecordWith, valueEquals }");
        await Assert.That(reading).DoesNotContain("this.x === other.x");
    }

    [Test]
    public async Task TypeParameterField_ComparesThroughDefaultEquals()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Box<T>(T Value);
            """
        );

        await Assert.That(files["box.ts"]).Contains("defaultEquals(this.value, other.value)");
        await Assert.That(files["box.ts"]).DoesNotContain("valueEquals");
    }

    [Test]
    public async Task ClassObjectAndInterfaceFields_CompareThroughDefaultEquals()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Account { public int Id; }

            [Transpile]
            public interface IShape { }

            [Transpile]
            public record Holder(Account Account, object Tag, IShape Shape);
            """
        );

        var holder = files["holder.ts"];

        await Assert.That(holder).Contains("defaultEquals(this.account, other.account)");
        await Assert.That(holder).Contains("defaultEquals(this.tag, other.tag)");
        await Assert.That(holder).Contains("defaultEquals(this.shape, other.shape)");
    }

    [Test]
    public async Task RecordField_KeepsValueEquals()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed record Inner(int N);

            [Transpile]
            public record Outer(Inner Inner);
            """
        );

        await Assert.That(files["outer.ts"]).Contains("valueEquals(this.inner, other.inner)");
    }

    [Test]
    public async Task IntegerStringAndBooleanFields_KeepStrictEquality()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Plain(int N, string S, bool B, long L);
            """
        );

        var plain = files["plain.ts"];

        await Assert.That(plain).Contains("this.n === other.n");
        await Assert.That(plain).Contains("this.s === other.s");
        await Assert.That(plain).Contains("this.b === other.b");
        await Assert.That(plain).DoesNotContain("valueEquals");
    }

    /// <summary>
    /// A plain struct compares by its fields on .NET (<c>ValueType.Equals</c>),
    /// so it carries a generated <c>equals</c> / <c>hashCode</c> that
    /// <c>defaultEquals</c> finds behind an <c>object</c> or interface field;
    /// no <c>with</c>, which only records synthesize. A struct that declares
    /// its own <c>Equals</c> / <c>GetHashCode</c> keeps it.
    /// </summary>
    [Test]
    public async Task PlainStruct_GetsFieldWiseEqualsAndHashCode()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Point { public int X; public double Y; public Point(int x, double y) { X = x; Y = y; } }

            [Transpile]
            public struct Tagged
            {
                public int N;
                public override bool Equals(object? obj) => obj is Tagged t && t.N == N;
                public override int GetHashCode() => N;
            }
            """
        );

        var point = files["point.ts"];

        await Assert
            .That(point)
            .Contains(
                "return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x && valueEquals(this.y, other.y);"
            );

        await Assert.That(point).Contains("hashCode(): number {");
        await Assert.That(point).DoesNotContain("with(");
        await Assert.That(point).DoesNotContain("copyRecordWith");
        await Assert.That(files["tagged.ts"]).DoesNotContain("HashCode");
        await Assert.That(files["tagged.ts"]).DoesNotContain("valueEquals");
    }

    /// <summary>
    /// A user method spelled like a synthesized value-equality member
    /// (<c>Equals(T other, double tol)</c>, <c>HashCode(int seed)</c>, a
    /// record's <c>With(int a)</c>) is emitted under its spec 013 fast-path name
    /// and its calls follow, so the synthesized <c>equals</c> / <c>hashCode</c> /
    /// <c>with</c> the runtime calls (<c>valueEquals</c>, <c>HashSet</c>, the
    /// <c>with</c> lowering) keep their names. A field <c>HashCode</c> keeps
    /// <c>hashCode</c> (no synthesized one, no <c>HashCode</c> import).
    /// </summary>
    [Test]
    public async Task UserMethodSpelledLikeAValueEqualityMember_TakesItsFastPathName()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record RTol(double V) { public bool Equals(RTol other, double tol) => System.Math.Abs(other.V - V) <= tol; }

            [Transpile]
            public record QWith(int A, string B) { public QWith With(int a) => new QWith(a, B); }

            [Transpile]
            public struct QHashM { public int V; public QHashM(int v) { V = v; } public int HashCode(int seed) => V + seed; }

            [Transpile]
            public struct KEntry { public int HashCode; public string Key; public KEntry(int h, string k) { HashCode = h; Key = k; } }

            [Transpile]
            public static class Use
            {
                public static bool Near(RTol a, RTol b) => a.Equals(b, 0.1) && a.Equals(b);
                public static QWith Copy(QWith a) => a with { A = 5 };
                public static QWith Rebuild(QWith a) => a.With(7);
                public static int Hash(QHashM a) => a.HashCode(2) + a.GetHashCode();
            }
            """
        );

        await Assert
            .That(files["r-tol.ts"])
            .Contains("equalsOtherTol(other: RTol, tol: number): boolean {");

        await Assert.That(files["r-tol.ts"]).Contains("equals(other: any): boolean {");
        await Assert.That(files["q-with.ts"]).Contains("withA(a: number): QWith {");
        await Assert.That(files["q-with.ts"]).Contains("with(overrides?: Partial<QWith>): QWith {");
        await Assert.That(files["q-hash-m.ts"]).Contains("hashCodeSeed(seed: number): number {");
        await Assert.That(files["q-hash-m.ts"]).Contains("hashCode(): number {");
        await Assert.That(files["k-entry.ts"]).DoesNotContain("hashCode(): number");
        await Assert.That(files["k-entry.ts"]).DoesNotContain("HashCode");

        var use = files["use.ts"];

        await Assert.That(use).Contains("a.equalsOtherTol(b, 0.1) && a.equals(b)");
        await Assert.That(use).Contains("a.with({ a: 5 })");
        await Assert.That(use).Contains("a.withA(7)");
        await Assert.That(use).Contains("a.hashCodeSeed(2)");
    }

    /// <summary>
    /// A field or property emitted as <c>equals</c> has no renaming path, and
    /// keeping it would drop the type's value equality: an error, never silent.
    /// </summary>
    [Test]
    public async Task DataMemberNamedEquals_ReportsAnError()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public struct QEqualsProp { public int A; public QEqualsProp(int a) { A = a; } public new bool Equals => true; }
            """
        );

        var error = diagnostics.Single(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.UnsupportedFeature
        );

        await Assert
            .That(error.Severity)
            .IsEqualTo(Metano.Compiler.Diagnostics.MetanoDiagnosticSeverity.Error);

        await Assert.That(error.Message).Contains("'QEqualsProp.Equals' is emitted as 'equals'");
    }
}
