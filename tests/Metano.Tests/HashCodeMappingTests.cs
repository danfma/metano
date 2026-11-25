namespace Metano.Tests;

/// <summary>
/// <c>HashCode</c> imports the runtime accumulator and <c>Combine</c> lowers to the variadic
/// <c>HashCode.combine</c> (#749). The golden type-checks under <c>tsgo --strict</c>.
/// </summary>
public class HashCodeMappingTests
{
    [Test]
    public async Task HashCode_ImportsTheRuntimeAccumulatorAndCombine()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Hashes
            {
                public int One(int a) => HashCode.Combine(a);
                public int Two(int a, string b) => HashCode.Combine(a, b);
                public int Eight(int a, int b, int c, int d, int e, int f, int g, int h) =>
                    HashCode.Combine(a, b, c, d, e, f, g, h);

                public int Accumulate(int a, string b)
                {
                    var hash = new HashCode();
                    hash.Add(a);
                    hash.Add(b);

                    return hash.ToHashCode();
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("hash-code-combine.ts", files["hashes.ts"]);
    }
}
