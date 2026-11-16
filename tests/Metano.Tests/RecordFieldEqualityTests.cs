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
}
