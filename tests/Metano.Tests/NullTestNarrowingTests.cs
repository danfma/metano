namespace Metano.Tests;

/// <summary>
/// A null test of a local narrows by itself in TypeScript, so the member read
/// it guards takes no <c>!</c> (#798).
/// </summary>
public class NullTestNarrowingTests
{
    private static string Emit(string source) =>
        TranspileHelper.Transpile(
            $$"""
            #nullable enable
            using System;
            public interface IPort { void Run(); }
            public sealed class ProbeNode { public ProbeNode? Next { get; set; } public int V; }
            [Transpile]
            public static class Probes
            {
                {{source}}
            }
            """
        )["probes.ts"];

    [Test]
    public async Task NullTestOfALocal_NarrowsWithoutAnAssertion()
    {
        var output = Emit(
            """
            public static bool OnLocal(Func<Exception?> get)
            {
                Exception? last = get();
                return last is not null && last.Message == "x";
            }

            public static string IfBranch(Func<Exception?> get)
            {
                Exception? last = get();
                if (last != null) { return last.Message; }
                return last is null ? "" : last.Message;
            }
            """
        );

        await Assert.That(output).Contains("return last != null && last.message === \"x\";");
        await Assert.That(output).DoesNotContain("last!");
    }

    [Test]
    public async Task ReassignedLocalReadInAClosure_KeepsTheAssertion()
    {
        var output = Emit(
            """
            public static string Captured(Func<Exception?> get)
            {
                Exception? cur = new Exception("x");
                Func<string> read = () => cur.Message;
                cur = get();

                return read();
            }

            public static string GuardedThenReassigned(Func<Exception?> get)
            {
                Exception? cur = get();

                if (cur != null)
                {
                    Func<string> read = () => cur.Message;
                    cur = null;

                    return read();
                }

                return "";
            }

            public static string NeverReassigned(Func<Exception?> get)
            {
                Exception? cur = get();

                if (cur == null) return "";

                Func<string> read = () => cur.Message;

                return read();
            }
            """
        );

        await Assert.That(output).Contains("() => cur!.message");
        await Assert.That(output).Contains("const read = () => cur.message;");
    }
}
