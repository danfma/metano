namespace Metano.Tests;

public class PrimaryCtorParamCoverageTests
{
    [Test]
    public async Task PrimaryCtorParam_AsSwitchExpressionScrutinee_ResolvesToField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Heading(string content, int level = 1)
            {
                public double Size()
                {
                    var sizeEm = level switch
                    {
                        1 => 2.0,
                        _ => 1.0,
                    };
                    return sizeEm + content.Length;
                }
            }
            """
        );

        var output = result["app/heading.ts"];
        await Assert.That(output).Contains("this._level === 1");
        await Assert.That(output).DoesNotContain(" level === 1");
        await Assert.That(output).DoesNotContain("(level === 1");
    }

    [Test]
    public async Task PrimaryCtorParam_AsMethodArgument_ResolvesToField()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Heading(int level)
            {
                public double Size() => Resolve(level);

                private static double Resolve(int level) => level * 1.5;
            }
            """
        );

        var output = result["app/heading.ts"];
        await Assert.That(output).Contains("Heading.resolve(this._level)");
    }
}
