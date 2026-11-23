namespace Metano.Tests;

/// <summary>
/// <c>await foreach</c> lowers to <c>for await</c>, and an <c>async</c>
/// iterator (<c>async IAsyncEnumerable&lt;T&gt;</c>), which no target lowers
/// yet, reports MS0001 once instead of emitting a synchronous generator
/// silently (#625).
/// </summary>
public class AsyncEnumerationTests
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Threading.Tasks;

        [Transpile]
        public static class Streams
        {
            public static async IAsyncEnumerable<int> Numbers()
            {
                for (var i = 0; i < 3; i++)
                {
                    await Task.Delay(1);
                    yield return i;
                }

                yield break;
            }

            public static async Task<int> Sum(IAsyncEnumerable<int> source)
            {
                var total = 0;
                await foreach (var x in source)
                    total += x;
                return total;
            }

            public static int SyncSum(List<int> source)
            {
                var total = 0;
                foreach (var x in source)
                    total += x;
                return total;
            }
        }
        """;

    [Test]
    public async Task AwaitForeach_LowersToForAwait()
    {
        var output = TranspileHelper.Transpile(Source)["streams.ts"];

        await Assert.That(output).Contains("for await (const x of source) {");
        await Assert.That(output).DoesNotContain("for (const x of source)");
    }

    [Test]
    public async Task AsyncIterator_ReportsUnsupportedOnce()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        var reported = diagnostics
            .Where(diagnostic => diagnostic.Message.Contains("Async iterator 'Numbers'"))
            .ToList();

        await Assert.That(reported).Count().IsEqualTo(1);

        await Assert
            .That(reported[0].Code)
            .IsEqualTo(Metano.Compiler.Diagnostics.DiagnosticCodes.UnsupportedFeature);
    }
}
