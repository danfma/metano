using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Spec 011 data-model § 2, B3: an array that reaches a list interface below
/// the top level of a conversion keeps its native shape, since only the outer
/// value is adopted (FR-003), so the TypeScript target reports MS0001 instead
/// of emitting <c>count</c> / <c>get</c> on a native array.
/// </summary>
public class NestedArrayListInterfaceTests
{
    [Test]
    [Arguments("IReadOnlyList<IReadOnlyList<int>>")]
    [Arguments("IEnumerable<IList<int>>")]
    [Arguments("IEnumerable<IReadOnlyCollection<int>>")]
    [Arguments("IList<ICollection<int>>")]
    public async Task JaggedArrayAssignedToNestedListInterface_ReportsMs0001(string target)
    {
        var diagnostics = UnsupportedFeatures(Body($"{target} view = jagged; Use(view);", target));

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Message).Contains("a nested array cannot be viewed");
    }

    [Test]
    [Arguments("IReadOnlyList<IReadOnlyList<int>>")]
    [Arguments("IEnumerable<IList<int>>")]
    [Arguments("IEnumerable<IReadOnlyCollection<int>>")]
    public async Task JaggedArrayPassedAsNestedListInterface_ReportsMs0001(string target)
    {
        var diagnostics = UnsupportedFeatures(Body("Use(jagged);", target));

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Message).Contains("a nested array cannot be viewed");
    }

    [Test]
    [Arguments("IEnumerable<int[]>", "Use(jagged);")]
    [Arguments("IReadOnlyList<int[]>", "Use(jagged);")]
    [Arguments("IReadOnlyList<int>", "Use(jagged[0]);")]
    public async Task ArrayWithoutNestedListInterface_ReportsNothing(string target, string body)
    {
        var diagnostics = UnsupportedFeatures(Body(body, target));

        await Assert.That(diagnostics).IsEmpty();
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(string source)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        return [.. diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)];
    }

    private static string Body(string body, string parameterType) =>
        $$"""
            using System.Collections.Generic;
            using Metano.Annotations;

            [Transpile]
            public static class Views
            {
                public static int Use({{parameterType}} view) => 0;

                public static void Run()
                {
                    int[][] jagged = [[1], [2, 3]];
                    {{body}}
                }
            }
            """;
}
