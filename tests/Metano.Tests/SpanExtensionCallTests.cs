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
            private const IEqualityComparer<int>? NoComparer = null;

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
    [Arguments("int[] xs", "xs.Contains(value: 4)", "xs.includes(4)")]
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

    /// <summary>
    /// A comparer that is omitted or the <c>null</c> constant asks for the
    /// default comparison: the call lowers as the value overload, without
    /// the comparer argument, also when a named argument would fill the
    /// omitted comparer with its default.
    /// </summary>
    [Test]
    [Arguments("int[] xs", "xs.IndexOf(4, null)", "xs.indexOf(4)")]
    [Arguments("int[] xs", "xs.IndexOf(4, comparer: null)", "xs.indexOf(4)")]
    [Arguments("int[] xs", "xs.IndexOf(4, default)", "xs.indexOf(4)")]
    [Arguments("int[] xs", "xs.IndexOf(4, (IEqualityComparer<int>?)null)", "xs.indexOf(4)")]
    [Arguments("int[] xs", "xs.IndexOf(4, NoComparer)", "xs.indexOf(4)")]
    [Arguments("int[] xs", "xs.LastIndexOf(1, null)", "xs.lastIndexOf(1)")]
    [Arguments("int[] xs", "xs.Contains(4, null)", "xs.includes(4)")]
    [Arguments("int?[] xs, int x", "xs.Contains(value: x)", "xs.includes(x)")]
    [Arguments("int?[] xs, int x", "xs.IndexOf(value: x)", "xs.indexOf(x)")]
    [Arguments("int?[] xs", "xs.LastIndexOf(value: (int?)null)", "xs.lastIndexOf(null)")]
    [Arguments("int?[] xs, int x", "xs.Contains(x, comparer: null)", "xs.includes(x)")]
    [Arguments("int?[] xs, int x", "xs.Contains(comparer: null, value: x)", "xs.includes(x)")]
    public async Task NullComparer_LowersAsTheValueOverload(
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

    /// <summary>
    /// A <c>ReadOnlySpan&lt;char&gt;</c> lowers to the string it views, so its
    /// searches and the parameterless <c>Trim</c> family lower to the string
    /// methods, as they did before #657; <c>Span&lt;T&gt;.Reverse()</c>
    /// reverses the viewed array in place.
    /// </summary>
    [Test]
    [Arguments("sp.StartsWith(\"ab\")", "sp.startsWith(\"ab\")")]
    [Arguments("sp.StartsWith('a')", "sp.startsWith(\"a\")")]
    [Arguments("sp.EndsWith(\"bc\")", "sp.endsWith(\"bc\")")]
    [Arguments("sp.EndsWith('c')", "sp.endsWith(\"c\")")]
    [Arguments("sp.IndexOf(\"ca\")", "sp.indexOf(\"ca\")")]
    [Arguments("sp.IndexOf('c')", "sp.indexOf(\"c\")")]
    [Arguments("sp.LastIndexOf(\"bc\")", "sp.lastIndexOf(\"bc\")")]
    [Arguments("sp.LastIndexOf('c')", "sp.lastIndexOf(\"c\")")]
    [Arguments("sp.Contains('c')", "sp.includes(\"c\")")]
    [Arguments("sp.Trim()", "sp.trim()")]
    [Arguments("sp.TrimStart()", "sp.trimStart()")]
    [Arguments("sp.TrimEnd()", "sp.trimEnd()")]
    [Arguments("Span<int> s = xs; s.Reverse()", "s.reverse()")]
    public async Task SpanOverStringOrArray_LowersToTheJavaScriptMethod(
        string statement,
        string expected
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Calling("ReadOnlySpan<char> sp, int[] xs", statement)
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains($"{expected};");
    }

    /// <summary>
    /// A tuple element compares by value in C#, but the lowered
    /// <c>includes</c> compares the tuple arrays by reference: the lookup
    /// reports, as a <c>List&lt;(int, int)&gt;</c> lookup does.
    /// </summary>
    [Test]
    public async Task TupleElement_ReportsTheValueComparison()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Returning("(int, int)[] pairs", "pairs.Contains((3, 4))")
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                "Expression 'pairs.Contains((3, 4))' compares C# tuples by value in an array or "
                    + "span; the tuples lower to arrays, which the emitted lookup compares by "
                    + "reference, so equal tuples are not found. Use a record as the element or "
                    + "key instead."
            );

        await Assert
            .That(files["calls.ts"])
            .Contains("return pairs.includes([3, 4] as [number, number]);");
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
    [Arguments("int[] xs, int x, IEqualityComparer<int>? same", "xs.Contains(x, comparer: same)")]
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
    [Arguments("int[] xs, int[] ys", "xs.IndexOf(ys)")]
    [Arguments("int[] xs, int[] ys", "xs.LastIndexOf(ys)")]
    [Arguments("char[] cs", "cs.IndexOf(\"bc\")")]
    [Arguments("char[] cs", "cs.StartsWith('a')")]
    [Arguments("Span<char> sc", "sc.StartsWith(\"ab\")")]
    [Arguments("Span<char> sc", "sc.Trim()")]
    [Arguments("ReadOnlySpan<char> sp", "sp.Trim('a')")]
    [Arguments(
        "ReadOnlySpan<char> sp",
        "sp.StartsWith(\"AB\", StringComparison.OrdinalIgnoreCase)"
    )]
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
                "var found = xs.Contains(x) && xs.SequenceEqual(ys) && xs.IndexOf(x, null) > 0"
            )
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();

        await Assert
            .That(files["calls.dart"])
            .Contains("xs.contains(x) && xs.sequenceEqual(ys) && xs.indexOf(x, null) > 0");
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
