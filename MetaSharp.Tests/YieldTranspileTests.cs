namespace MetaSharp.Tests;

public class YieldTranspileTests
{
    [Test]
    public async Task ClassMethod_WithYield_EmitsGeneratorMethod()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class SequenceFactory
            {
                public System.Collections.Generic.IEnumerable<int> Values(int x)
                {
                    yield return 1;
                    if (x < 0) yield break;
                    yield return x;
                }
            }
            """
        );

        var output = result["SequenceFactory.ts"];
        await Assert.That(output).Contains("*values(x: number): Generator<number>");
        await Assert.That(output).Contains("yield 1;");
        await Assert.That(output).Contains("yield x;");
        await Assert.That(output).Contains("return;");
    }

    [Test]
    public async Task ExportedAsModule_WithYield_EmitsGeneratorFunction()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class SequenceModule
            {
                public static System.Collections.Generic.IEnumerable<string> Names()
                {
                    yield return "alpha";
                    yield return "beta";
                }
            }
            """
        );

        var output = result["SequenceModule.ts"];
        await Assert.That(output).Contains("export function* names(): Generator<string>");
        await Assert.That(output).Contains("yield \"alpha\";");
        await Assert.That(output).Contains("yield \"beta\";");
    }

    [Test]
    public async Task IEnumerableWithoutYield_RemainsArrayMapping()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class SnapshotProvider
            {
                public System.Collections.Generic.IEnumerable<int> Snapshot() => [1, 2, 3];
            }
            """
        );

        var output = result["SnapshotProvider.ts"];
        await Assert.That(output).Contains("snapshot(): number[]");
        await Assert.That(output).DoesNotContain("*snapshot(");
    }
}
