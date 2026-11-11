using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// C# 14 binds <c>array.Contains(x)</c>, <c>array.SequenceEqual(other)</c> and
/// the other span extensions to <c>System.MemoryExtensions</c> through the
/// implicit array-to-span conversion (#657). The value overloads of
/// <c>Contains</c>, <c>IndexOf</c> and <c>LastIndexOf</c> lower as their
/// <c>List&lt;T&gt;</c> counterparts do; every other <c>MemoryExtensions</c>
/// call reports MS0001 at the method name and lowers to the throwing
/// placeholder instead of a member call a JavaScript array does not have.
/// </summary>
public class SpanExtensionCallTests
{
    private const string Host = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Metano.Annotations;

        [Transpile]
        public record Point(int X, int Y);

        [Transpile]
        public class Calls
        {
        """;

    [Test]
    [Arguments("int[] xs, int x", "xs.Contains(x)", "xs.includes(x)")]
    [Arguments("string[] xs, string x", "xs.Contains(x)", "xs.includes(x)")]
    [Arguments("Point[] xs, Point x", "xs.Contains(x)", "xs.includes(x)")]
    [Arguments("long[] xs, long x", "xs.Contains(x)", "xs.includes(x)")]
    [Arguments("double[] xs", "xs.Contains(double.NaN)", "xs.includes(Number.NaN)")]
    [Arguments("int?[] xs", "xs.Contains(null)", "xs.includes(null)")]
    [Arguments("string?[] xs", "xs.Contains(null)", "xs.includes(null)")]
    [Arguments("int[] xs, int x", "xs?.Contains(x) ?? false", "xs?.includes(x) ?? false")]
    [Arguments("int[] xs, int x", "xs.IndexOf(x)", "xs.indexOf(x)")]
    [Arguments("int?[] xs, int x", "xs.IndexOf(x)", "xs.indexOf(x)")]
    [Arguments("int[] xs, int x", "xs.LastIndexOf(x)", "xs.lastIndexOf(x)")]
    public async Task ValueOverload_LowersAsTheListMethod(
        string parameters,
        string call,
        string expected
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Returning(parameters, call)
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains($"return {expected};");
    }

    [Test]
    [Arguments("List<int> xs, int x", "xs.Contains(x)", "xs.includes(x)")]
    [Arguments("List<int> xs, int x", "xs.IndexOf(x)", "xs.indexOf(x)")]
    [Arguments("IEnumerable<int> xs, int x", "xs.Contains(x)", "linq(xs, contains(x))")]
    [Arguments("IReadOnlyList<int> xs, int x", "xs.Contains(x)", "linq(xs, contains(x))")]
    public async Task CollectionContains_KeepsItsLowering(
        string parameters,
        string call,
        string expected
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Returning(parameters, call)
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains($"return {expected};");
    }

    [Test]
    [Arguments("int[] xs, int[] ys", "xs.SequenceEqual(ys)")]
    [Arguments("int[] xs, int[] ys, IEqualityComparer<int> same", "xs.SequenceEqual(ys, same)")]
    [Arguments("string[] xs, string[] ys", "xs.SequenceEqual(ys)")]
    [Arguments("int[] xs, int x, IEqualityComparer<int> same", "xs.Contains(x, same)")]
    [Arguments("int[] xs, int x", "xs.Contains(x, null)")]
    [Arguments("int[] xs, int x, IEqualityComparer<int> same", "xs.IndexOf(x, same)")]
    [Arguments("int[] xs, int x", "MemoryExtensions.Contains(xs, x)")]
    [Arguments("int[] xs, int[] ys", "xs.StartsWith(ys)")]
    [Arguments("int[] xs, int x", "xs.StartsWith(x)")]
    [Arguments("int[] xs, int[] ys", "xs.EndsWith(ys)")]
    [Arguments("int[] xs, int x", "xs.EndsWith(x)")]
    [Arguments("int[] xs", "xs.Sort()")]
    [Arguments("int[] xs, int x", "xs.BinarySearch(x)")]
    [Arguments("int[] xs, int x", "xs.IndexOfAny(x, x)")]
    [Arguments("int[] xs, int[] ys", "xs.IndexOfAny(ys)")]
    [Arguments("int[] xs, int x", "xs.ContainsAny(x, x)")]
    [Arguments("int[] xs, int x", "xs.Count(x)")]
    [Arguments("int[] xs", "xs.AsSpan()")]
    [Arguments("string s", "s.Count('a')")]
    [Arguments("string s", "s.IndexOfAny('a', 'b')")]
    public async Task UnmappedOverload_ReportsAtTheMethodName(string parameters, string call)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Calling(parameters, call)
        );

        var report = UnsupportedFeatures(diagnostics).Single();
        var method = MethodNameOf(call);

        await Assert
            .That(report.Message)
            .IsEqualTo(
                $"The span method 'MemoryExtensions.{method}', which C# binds for an array, "
                    + "span or string receiver, has no counterpart for this overload; the call "
                    + "throws at run time."
            );

        await Assert.That(report.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo(method);

        await Assert
            .That(files["calls.ts"])
            .Contains("throw new Error(\"Unsupported expression: UnloweredSpanCall\");");
    }

    /// <summary>
    /// The Dart target declares no mappings port, so it reports none of
    /// these calls and keeps emitting them by name, as before #657.
    /// </summary>
    [Test]
    public async Task SpanCalls_AreUnchangedInDart()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            Calling(
                "int[] xs, int[] ys, int x",
                "var found = xs.Contains(x) && xs.SequenceEqual(ys)"
            )
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.dart"]).Contains("xs.contains(x) && xs.sequenceEqual(ys)");
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

    /// <summary>The name of the method <paramref name="call"/> invokes.</summary>
    private static string MethodNameOf(string call)
    {
        var name = call[(call.IndexOf('.') + 1)..];

        return name[..name.IndexOf('(')];
    }

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature);
}
