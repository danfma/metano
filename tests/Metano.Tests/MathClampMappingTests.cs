namespace Metano.Tests;

/// <summary>
/// <c>Math.Clamp</c> lowers to runtime helpers that keep each overload's numeric kind (#750).
/// The golden type-checks under <c>tsgo --strict</c>.
/// </summary>
public class MathClampMappingTests
{
    [Test]
    public async Task MathClamp_KeepsTheNumericKindOfEachOverload()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Clamps
            {
                public int Int(int v) => Math.Clamp(v, 0, 10);
                public double Double(double v) => Math.Clamp(v, 0.5, 1.5);
                public float Single(float v) => Math.Clamp(v, 0f, 1f);
                public byte Byte(byte v) => Math.Clamp(v, (byte)1, (byte)9);
                public long Long(long v) => Math.Clamp(v, 0L, 10L);
                public decimal Decimal(decimal v) => Math.Clamp(v, 0m, 1m);
            }
            """
        );

        await TranspileHelper.AssertGolden("math-clamp.ts", files["clamps.ts"]);
    }
}
