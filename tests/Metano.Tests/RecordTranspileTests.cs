namespace Metano.Tests;

public class RecordTranspileTests
{
    [Test]
    public async Task SimpleRecord_GeneratesClass()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Point(int X, int Y);
            """
        );

        await TranspileHelper.AssertGolden("simple-record.ts", result["point.ts"]);
    }

    [Test]
    public async Task RecordWithMethods_GeneratesClassWithMethods()
    {
        // Uses int (not decimal) so the assertion stays focused on record-class shape
        // and doesn't accidentally exercise the decimal → Decimal mapping introduced
        // for the decimal.js integration.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Amount(int Value)
            {
                public int Doubled() => Value * 2;

                public static Amount FromValue(int value) => new(value);
            }
            """
        );

        await TranspileHelper.AssertGolden("record-with-methods.ts", result["amount.ts"]);
    }

    [Test]
    public async Task InstanceMethod_QualifiesWithSelfParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Amount(int Value)
            {
                public int Doubled() => Value * 2;
            }
            """
        );

        // Instance methods use 'this' now (class-based pattern)
        await Assert.That(result["amount.ts"]).Contains("this.value");
    }
}
