namespace Metano.Tests;

/// <summary>
/// A record field whose <c>===</c> is not .NET's
/// <c>EqualityComparer&lt;T&gt;.Default</c> compares through the runtime's
/// <c>valueEquals</c> (#674): a <c>double</c> / <c>float</c>, whose <c>NaN</c>
/// equals itself on .NET, and a type parameter, which can hold a
/// <c>TimeSpan</c> or a <c>decimal</c>.
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
    public async Task TypeParameterField_ComparesThroughValueEquals()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Box<T>(T Value);
            """
        );

        await Assert.That(files["box.ts"]).Contains("valueEquals(this.value, other.value)");
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
}
