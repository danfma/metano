namespace MetaSharp.Tests;

public class NullableTranspileTests
{
    // ─── Nullable value types ───────────────────────────────

    [Test]
    public async Task NullableInt_MapsToNumberOrNull()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Score(int? Value);
            """
        );

        var output = result["score.ts"];
        await Assert.That(output).Contains("value: number | null");
    }

    [Test]
    public async Task NullableBool_MapsToBooleanOrNull()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Flag(bool? Active);
            """
        );

        var output = result["flag.ts"];
        await Assert.That(output).Contains("active: boolean | null");
    }

    // ─── Nullable reference types ───────────────────────────

    [Test]
    public async Task NullableString_MapsToStringOrNull()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile]
            public record Person(string Name, string? Nickname);
            """
        );

        var output = result["person.ts"];
        await Assert.That(output).Contains("name: string");
        await Assert.That(output).Contains("nickname: string | null");
    }

    [Test]
    public async Task NullableCustomType_MapsToTypeOrNull()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            namespace App
            {
                [Transpile]
                public record Address(string Street);

                [Transpile]
                public record Person(string Name, Address? Address);
            }
            """
        );

        var output = result["person.ts"];
        await Assert.That(output).Contains("address: Address | null");
    }

    // ─── Nullable in method signatures ──────────────────────

    [Test]
    public async Task NullableReturnType_MapsCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, ExportedAsModule]
            public static class Finder
            {
                public static string? Find(string key)
                {
                    return null;
                }
            }
            """
        );

        var output = result["finder.ts"];
        await Assert.That(output).Contains("): string | null");
    }

    [Test]
    public async Task NullableParameter_MapsCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, ExportedAsModule]
            public static class Formatter
            {
                public static string Format(string? input)
                {
                    return input ?? "default";
                }
            }
            """
        );

        var output = result["formatter.ts"];
        await Assert.That(output).Contains("input: string | null");
    }

    // ─── Null-coalescing ────────────────────────────────────

    [Test]
    public async Task NullCoalescing_MapsToDoubleQuestion()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, ExportedAsModule]
            public static class Utils
            {
                public static string OrDefault(string? value)
                {
                    return value ?? "fallback";
                }
            }
            """
        );

        var output = result["utils.ts"];
        await Assert.That(output).Contains("value ?? \"fallback\"");
    }

    // ─── Null-conditional (?.) ──────────────────────────────

    [Test]
    public async Task NullConditionalAccess_MapsToOptionalChaining()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, ExportedAsModule]
            public static class Helper
            {
                public static int? GetLength(string? value)
                {
                    return value?.Length;
                }
            }
            """
        );

        var output = result["helper.ts"];
        await Assert.That(output).Contains("value?.length");
    }

    // ─── Nullable generic types ─────────────────────────────

    [Test]
    public async Task NullableGenericField_MapsCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile]
            public record Container<T>(T Value, T? Optional) where T : class;
            """
        );

        var output = result["container.ts"];
        await Assert.That(output).Contains("value: T");
        await Assert.That(output).Contains("optional: T | null");
    }
}
