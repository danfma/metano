namespace Metano.Tests;

/// <summary>
/// A property pattern entry reads its member through the declarative BCL
/// mappings, like an ordinary member read: <c>{ InnerException: null }</c> on
/// an <c>Exception</c> and <c>{ LongLength: 2 }</c> on an array read what the
/// mapping lowers to, not a JS property that does not exist (#357).
/// </summary>
public class PropertySubpatternMappingTests
{
    [Test]
    public async Task MappedMembers_ReadThroughTheirMapping()
    {
        var output = TranspileProbe(
            """
            public static bool B(System.Exception e) => e is { InnerException: null };
            public static bool C(int[] a) => a is { LongLength: 2 };
            """
        );

        await Assert
            .That(output)
            .Contains("return e != null && (e.cause instanceof Error ? e.cause : null) == null;");

        await Assert.That(output).Contains("return a != null && BigInt(a.length) === 2n;");
        await Assert.That(output).DoesNotContain(".innerException");
        await Assert.That(output).DoesNotContain(".longLength");
    }

    [Test]
    public async Task ExtendedMemberPath_MapsEveryLevel()
    {
        var output = TranspileProbe(
            """
            public sealed record Holder(System.Exception Err, int[] Arr);
            public static bool E(Holder h) => h is { Err.InnerException: null, Arr.LongLength: 1 };
            """
        );

        await Assert
            .That(output)
            .Contains("(h.err.cause instanceof Error ? h.err.cause : null) == null");

        await Assert.That(output).Contains("BigInt(h.arr.length) === 1n");
    }

    [Test]
    public async Task Designator_ReadsTheMappedMember()
    {
        var output = TranspileProbe(
            """
            public static string Q(System.Exception e) =>
                e is { InnerException: var inner } ? inner?.Message ?? "none" : "x";
            """
        );

        await Assert
            .That(output)
            .Contains("(inner = (e.cause instanceof Error ? e.cause : null), true)");
    }

    [Test]
    public async Task UnmappedMembers_KeepTheirNamingPolicy()
    {
        var output = TranspileProbe(
            """
            public static bool G(int[] a) => a is { Length: 2 };
            public static bool S(string s) => s is { Length: 0 };
            """
        );

        await Assert.That(output).Contains("return a != null && a.length === 2;");
        await Assert.That(output).Contains("return s != null && s.length === 0;");
    }

    private static string TranspileProbe(string members)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public static class Probe
            {
            {{members}}
            }
            """
        );

        return result["probe.ts"];
    }
}
