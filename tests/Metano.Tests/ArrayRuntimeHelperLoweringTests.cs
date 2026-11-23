using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Wave 6 group A (#663, #664, #687, #712): the sorts and searches of <c>System.Array</c>
/// lower to <c>metano-runtime</c> helpers, because JavaScript's <c>sort()</c> orders by text and
/// its <c>indexOf</c> compares by identity; <c>StringComparer.Ordinal</c> lowers to the runtime
/// comparer; <c>ImmutableArray&lt;T&gt;.IsDefaultOrEmpty</c> is <c>isEmpty</c>.
/// </summary>
public class ArrayRuntimeHelperLoweringTests
{
    private const string Host = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using Metano.Annotations;

        [Transpile]
        public class Calls
        {
        """;

    [Test]
    [Arguments("int[] xs", "Array.Sort(xs)", "sortArray(xs)")]
    [Arguments(
        "int[] xs",
        "Array.Sort(xs, (x, y) => y - x)",
        "sortArray(xs, (x: number, y: number) => (y - x) | 0)"
    )]
    [Arguments("int[] xs, IComparer<int> c", "Array.Sort(xs, c)", "sortArray(xs, c)")]
    [Arguments("int[] ks, string[] vs", "Array.Sort(ks, vs)", "sortArrayByKeys(ks, vs)")]
    [Arguments(
        "int[] ks, string[] vs, IComparer<int> c",
        "Array.Sort(ks, vs, c)",
        "sortArrayByKeys(ks, vs, c)"
    )]
    public async Task ArraySort_LowersToTheRuntimeHelper(
        string parameters,
        string statement,
        string expected
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Calling(parameters, statement)
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains($"{expected};");
    }

    [Test]
    [Arguments("Array.IndexOf(xs, x)", "arrayIndexOf(xs, x)")]
    [Arguments("Array.IndexOf(xs, x, 1)", "arrayIndexOf(xs, x, 1)")]
    [Arguments("Array.LastIndexOf(xs, x)", "arrayLastIndexOf(xs, x)")]
    public async Task ArraySearch_LowersToTheValueEqualityHelper(string call, string expected)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Returning("string[] xs, string x", call)
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains($"return {expected};");
    }

    private static string Returning(string parameters, string expression) =>
        Host
        + $$"""
                public object Run({{parameters}}) => {{expression}};
            }
            """;

    private static string Calling(string parameters, string statement) =>
        Host
        + $$"""
                public void Run({{parameters}})
                {
                    {{statement}};
                }
            }
            """;

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature);
}
