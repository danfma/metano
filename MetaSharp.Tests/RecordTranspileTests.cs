namespace MetaSharp.Tests;

public class RecordTranspileTests
{
    [Test]
    public async Task SimpleRecord_GeneratesClass()
    {
        var result = TranspileHelper.Transpile("""
            [Transpile]
            public readonly record struct Point(int X, int Y);
            """);

        var expected = TranspileHelper.ReadExpected("simple-record.ts");
        await Assert.That(result["point.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task RecordWithMethods_GeneratesClassWithMethods()
    {
        var result = TranspileHelper.Transpile("""
            [Transpile]
            public readonly record struct Amount(decimal Value)
            {
                public decimal Doubled() => Value * 2;

                public static Amount FromValue(decimal value) => new(value);
            }
            """);

        var expected = TranspileHelper.ReadExpected("record-with-methods.ts");
        await Assert.That(result["amount.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task InstanceMethod_QualifiesWithSelfParameter()
    {
        var result = TranspileHelper.Transpile("""
            [Transpile]
            public readonly record struct Amount(decimal Value)
            {
                public decimal Doubled() => Value * 2;
            }
            """);

        // Instance methods use 'this' now (class-based pattern)
        await Assert.That(result["amount.ts"]).Contains("this.value");
    }
}
