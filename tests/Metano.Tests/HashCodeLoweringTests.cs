namespace Metano.Tests;

/// <summary>
/// Every <c>GetHashCode()</c> call lowers to the runtime <c>valueHashCode</c>,
/// whichever override it binds to, and a <c>GetHashCode</c> override is
/// declared as <c>hashCode</c> — the name records synthesize and the runtime
/// (<c>valueHashCode</c>, <c>HashSet</c>) calls (#254). The static
/// <c>object.Equals(a, b)</c> / <c>ReferenceEquals(a, b)</c> map to
/// <c>valueEquals</c> / <c>===</c>.
/// </summary>
public class HashCodeLoweringTests
{
    [Test]
    public async Task HashCodesAndObjectEquality_MatchTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public record Point(int X, int Y);

            [Transpile]
            public enum Color { Red, Green }

            [Transpile]
            public class Money
            {
                public int Cents;

                public override bool Equals(object? other) => other is Money && ((Money)other).Cents == Cents;

                public override int GetHashCode() => Cents.GetHashCode() ^ base.GetHashCode();

                public int Hashes(Point p, string s, int i, Color c, object o, int? n) =>
                    p.GetHashCode() + s.GetHashCode() + i.GetHashCode() + c.GetHashCode()
                    + o.GetHashCode() + n.GetHashCode() + GetHashCode() + this.GetHashCode();

                public bool Same(Money? a, Money? b) => Equals(a, b) && !ReferenceEquals(a, b);
            }

            [Transpile]
            public class Coins : Money
            {
                public override int GetHashCode() => base.GetHashCode() + 1;
            }
            """
        );

        await Assert
            .That(result["money.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("hash-code-lowering.ts"));
        await Assert.That(result["coins.ts"]).Contains("return super.hashCode() + 1;");
    }

    [Test]
    public async Task RecordDeclaringEqualsAndGetHashCode_SkipsTheSynthesizedOnes()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable

            [Transpile]
            public record Tag(string Name)
            {
                public virtual bool Equals(Tag? other) => other is not null && Name.ToUpperInvariant() == other.Name.ToUpperInvariant();

                public override int GetHashCode() => Name.ToUpperInvariant().GetHashCode();
            }
            """
        );

        var output = result["tag.ts"];

        await Assert.That(CountOccurrences(output, "  equals(")).IsEqualTo(1);
        await Assert.That(output).Contains("equals(other: Tag | null): boolean {");
        await Assert.That(CountOccurrences(output, "  hashCode(): number {")).IsEqualTo(1);
        await Assert.That(output).Contains("return valueHashCode(this.name.toUpperCase());");
        await Assert.That(output).DoesNotContain("getHashCode");
        await Assert
            .That(output)
            .Contains("import { copyRecordWith, valueHashCode } from \"metano-runtime\";");
    }

    private static int CountOccurrences(string text, string token) => text.Split(token).Length - 1;
}
