namespace Metano.Tests;

/// <summary>
/// #769: <c>string.Equals</c> / <c>string.Compare</c> with a <c>StringComparison</c>, and
/// the overloads that imply one, lower to the <c>stringEquals</c> / <c>stringCompare</c>
/// runtime helpers instead of a call by name that does not exist at run time.
/// </summary>
public class StringComparisonMappingTests
{
    private const string Source = """
        using System;
        using Metano.Annotations;

        [Transpile]
        public static class Texts
        {
            public static bool StaticEquals(string? x, string? y) =>
                string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

            public static bool InstanceEquals(string x, string? y) =>
                x.Equals(y, StringComparison.InvariantCulture);

            public static bool PlainStaticEquals(string? x, string? y) => string.Equals(x, y);

            public static bool PlainInstanceEquals(string x, string? y) => x.Equals(y);

            public static bool ObjectInstanceEquals(string x, object? y) => x.Equals(y);

            public static int CompareWith(string? x, string? y) =>
                string.Compare(x, y, StringComparison.Ordinal);

            public static int CompareCulture(string? x, string? y) => string.Compare(x, y);

            public static int CompareIgnoringCase(string? x, string? y, bool ignoreCase) =>
                string.Compare(x, y, ignoreCase);

            public static int CompareOrdinal(string? x, string? y) => string.CompareOrdinal(x, y);

            public static int CompareTo(string x, string? y) => x.CompareTo(y);
        }
        """;

    [Test]
    public async Task ComparisonOverloads_LowerToRuntimeHelpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);
        var output = files["texts.ts"];

        await Assert.That(diagnostics).IsEmpty();

        await Assert
            .That(output)
            .Contains(
                "import { StringComparison, stringCompare, stringEquals } from \"metano-runtime\";"
            );

        await Assert
            .That(output)
            .Contains("return stringEquals(x, y, StringComparison.OrdinalIgnoreCase);");

        await Assert
            .That(output)
            .Contains("return stringEquals(x, y, StringComparison.InvariantCulture);");

        await Assert.That(output).Contains("return (x === y);");
        await Assert.That(output).Contains("return stringCompare(x, y, StringComparison.Ordinal);");

        await Assert
            .That(output)
            .Contains("return stringCompare(x, y, StringComparison.CurrentCulture);");

        await Assert
            .That(output)
            .Contains(
                "return stringCompare(x, y, ignoreCase ? StringComparison.CurrentCultureIgnoreCase "
                    + ": StringComparison.CurrentCulture);"
            );

        await Assert.That(output).DoesNotContain(".equals(");
        await Assert.That(output).DoesNotContain(".compareTo(");
        await Assert.That(output).DoesNotContain("String.");
    }

    [Test]
    public async Task ComparisonOverloads_TypeCheckStrictly()
    {
        var files = TranspileHelper.Transpile(Source);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }
}
