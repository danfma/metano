namespace Metano.Tests;

/// <summary>
/// Lowering of <c>CollectionExtensions.GetValueOrDefault</c>
/// (<c>src/Metano/Runtime/Dictionaries.cs</c>). JS <c>Map</c> has no
/// <c>getValueOrDefault</c>, so every overload reaches the runtime
/// <c>getValueOrDefault(map, key, defaultValue)</c> helper, with
/// <c>default(TValue)</c> synthesized for the single-argument overload.
/// </summary>
public class GetValueOrDefaultMappingTests
{
    [Test]
    public async Task SingleArgumentOverload_PassesDefaultOfValueType()
    {
        var output = TranspileLookups(
            """
            public static int Count(Dictionary<string, int> d) => d.GetValueOrDefault("a");
            public static double Ratio(Dictionary<string, double> d) => d.GetValueOrDefault("a");
            public static long Total(Dictionary<string, long> d) => d.GetValueOrDefault("a");
            public static bool Flag(Dictionary<string, bool> d) => d.GetValueOrDefault("a");
            public static decimal Price(Dictionary<string, decimal> d) => d.GetValueOrDefault("a");
            public static Level Tier(Dictionary<string, Level> d) => d.GetValueOrDefault("a");
            """
        );

        await Assert
            .That(output)
            .Contains(
                "static count(d: Map<string, number>): number {\n    return getValueOrDefault(d, \"a\", 0);"
            );
        await Assert
            .That(output)
            .Contains(
                "static ratio(d: Map<string, number>): number {\n    return getValueOrDefault(d, \"a\", 0);"
            );
        await Assert
            .That(output)
            .Contains(
                "static total(d: Map<string, number>): number {\n    return getValueOrDefault(d, \"a\", 0);"
            );
        await Assert.That(output).Contains("return getValueOrDefault(d, \"a\", false);");
        await Assert
            .That(output)
            .Contains("return getValueOrDefault(d, \"a\", new Decimal(\"0\"));");
        await Assert.That(output).Contains("return getValueOrDefault(d, \"a\", Level.Low);");
        await Assert.That(output).DoesNotContain(".getValueOrDefault(");
    }

    [Test]
    public async Task SingleArgumentOverload_PassesNullForReferenceAndNullableValues()
    {
        var output = TranspileLookups(
            """
            public static string? Name(IReadOnlyDictionary<int, string> d) => d.GetValueOrDefault(1);
            public static int? Maybe(Dictionary<int, int?> d) => d.GetValueOrDefault(1);
            """
        );

        await Assert
            .That(output)
            .Contains(
                "static name(d: Map<number, string>): string | null {\n    return getValueOrDefault(d, 1, null);"
            );
        await Assert
            .That(output)
            .Contains(
                "static maybe(d: Map<number, number | null>): number | null {\n    return getValueOrDefault(d, 1, null);"
            );
    }

    [Test]
    public async Task SingleArgumentOverload_PassesTypedDefaultForCharAndGuid()
    {
        var output = TranspileLookups(
            """
            public static char Initial(Dictionary<string, char> d) => d.GetValueOrDefault("a");
            public static System.Guid Id(Dictionary<string, System.Guid> d) => d.GetValueOrDefault("a");
            """
        );

        await Assert.That(output).Contains("return getValueOrDefault(d, \"a\", \"\\u0000\");");
        await Assert.That(output).Contains("return getValueOrDefault(d, \"a\", UUID.empty);");
        await Assert.That(output).DoesNotContain("undefined");
    }

    [Test]
    public async Task SingleArgumentOverload_FindsTheZeroMemberOfAnUnsignedEnum()
    {
        var output = TranspileLookups(
            """
            public static Wide Mask(Dictionary<string, Wide> d) => d.GetValueOrDefault("a");
            """
        );

        await Assert.That(output).Contains("return getValueOrDefault(d, \"a\", Wide.None);");
    }

    [Test]
    public async Task DefaultValueOverload_PassesTheCallerDefault()
    {
        var output = TranspileLookups(
            """
            public static int Count(Dictionary<string, int> d) => d.GetValueOrDefault("a", 5);
            public static int Static(Dictionary<string, int> d) =>
                CollectionExtensions.GetValueOrDefault(d, "a", 7);
            """
        );

        await Assert.That(output).Contains("return getValueOrDefault(d, \"a\", 5);");
        await Assert.That(output).Contains("return getValueOrDefault(d, \"a\", 7);");
        await Assert.That(output).DoesNotContain("CollectionExtensions");
        await Assert.That(output).Contains("import { getValueOrDefault } from \"metano-runtime\";");
    }

    private static string TranspileLookups(string members)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public enum Level { Low, High }

            [Transpile]
            public enum Wide : ulong { Top = ulong.MaxValue, None = 0 }

            [Transpile]
            public static class Lookups
            {
            {{members}}
            }
            """
        );

        return result["lookups.ts"];
    }
}
