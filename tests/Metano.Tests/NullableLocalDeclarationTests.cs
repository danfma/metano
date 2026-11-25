using Metano.Tests.TreeShaking;

namespace Metano.Tests;

/// <summary>
/// A reassigned local declared nullable keeps its declared type when its
/// initializer is not <c>null</c> (#774): TypeScript would otherwise type it by
/// the initializer and reject the later <c>null</c> store under <c>--strict</c>.
/// </summary>
public class NullableLocalDeclarationTests
{
    private const string Source = """
        #nullable enable

        [Transpile]
        public sealed class Link
        {
            public Link? NextSub;
            public int Value;
        }

        [Transpile]
        public static class Walker
        {
            public static int Walk(Link link)
            {
                var total = 0;
                Link? current = link;

                while (current is not null)
                {
                    total += current.Value;
                    current = current.NextSub;
                }

                return total;
            }

            public static int? Last(int start)
            {
                int? value = start;
                value = null;
                return value;
            }

            public static int Inferred(Link link)
            {
                var current = link;
                Link? fixedOne = link;
                return current.Value + fixedOne.Value;
            }
        }
        """;

    [Test]
    public async Task ReassignedNullableLocal_KeepsItsDeclaredType()
    {
        var output = TranspileHelper.Transpile(Source)["walker.ts"];

        await Assert.That(output).Contains("let current: Link | null = link;");
        await Assert.That(output).Contains("let value: number | null = start;");
        await Assert.That(output).Contains("const current = link;");
        await Assert.That(output).Contains("const fixedOne = link;");
    }

    [Test]
    public async Task ReassignedNullableLocal_TypeChecksUnderStrict()
    {
        var output = TranspileHelper.Transpile(Source);

        var (exitCode, log) = await StrictTypeCheck.Run(new Dictionary<string, string>(), output);

        await Assert.That(log).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }
}
