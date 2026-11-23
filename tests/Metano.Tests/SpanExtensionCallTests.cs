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

            private static int Descending(int x, int y) => y - x;

        """;

    [Test]
    [Arguments("int[] xs, int x", "xs.Contains(x)", "arrayContains(xs, x)")]
    [Arguments("string[] xs, string x", "xs.Contains(x)", "arrayContains(xs, x)")]
    [Arguments("Point[] xs, Point x", "xs.Contains(x)", "arrayContains(xs, x)")]
    [Arguments("long[] xs, long x", "xs.Contains(x)", "arrayContains(xs, x)")]
    [Arguments("double[] xs", "xs.Contains(double.NaN)", "arrayContains(xs, Number.NaN)")]
    [Arguments("int?[] xs", "xs.Contains(null)", "arrayContains(xs, null)")]
    [Arguments("string?[] xs", "xs.Contains(null)", "arrayContains(xs, null)")]
    [Arguments(
        "int[] xs, int x",
        "xs?.Contains(x) ?? false",
        "(xs == null ? null : arrayContains(xs, x)) ?? false"
    )]
    [Arguments("int[] xs, int x", "xs.IndexOf(x)", "arrayIndexOf(xs, x)")]
    [Arguments("int?[] xs, int x", "xs.IndexOf(x)", "arrayIndexOf(xs, x)")]
    [Arguments("int[] xs, int x", "xs.LastIndexOf(x)", "arrayLastIndexOf(xs, x)")]
    [Arguments("int[] xs", "xs.Contains(value: 4)", "arrayContains(xs, 4)")]
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
    [Arguments("int[] xs", "xs.IndexOf(4, null)", "arrayIndexOf(xs, 4)")]
    [Arguments("int[] xs", "xs.IndexOf(4, comparer: null)", "arrayIndexOf(xs, 4)")]
    [Arguments("int[] xs", "xs.IndexOf(4, default)", "arrayIndexOf(xs, 4)")]
    [Arguments("int[] xs", "xs.IndexOf(4, (IEqualityComparer<int>?)null)", "arrayIndexOf(xs, 4)")]
    [Arguments("int[] xs", "xs.IndexOf(4, NoComparer)", "arrayIndexOf(xs, 4)")]
    [Arguments("int[] xs", "xs.LastIndexOf(1, null)", "arrayLastIndexOf(xs, 1)")]
    [Arguments("int[] xs", "xs.Contains(4, null)", "arrayContains(xs, 4)")]
    [Arguments("int?[] xs, int x", "xs.Contains(value: x)", "arrayContains(xs, x)")]
    [Arguments("int?[] xs, int x", "xs.IndexOf(value: x)", "arrayIndexOf(xs, x)")]
    [Arguments("int?[] xs", "xs.LastIndexOf(value: (int?)null)", "arrayLastIndexOf(xs, null)")]
    [Arguments("int?[] xs, int x", "xs.Contains(x, comparer: null)", "arrayContains(xs, x)")]
    [Arguments("int?[] xs, int x", "xs.Contains(comparer: null, value: x)", "arrayContains(xs, x)")]
    [Arguments("string s", "s.StartsWith('b', null)", "s.startsWith(\"b\")")]
    [Arguments("string s", "s.EndsWith('b', comparer: null)", "s.endsWith(\"b\")")]
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
    [Arguments("sp.IndexOf(\"ca\")", "arrayIndexOf(sp, \"ca\")")]
    [Arguments("sp.IndexOf('c')", "arrayIndexOf(sp, \"c\")")]
    [Arguments("sp.LastIndexOf(\"bc\")", "arrayLastIndexOf(sp, \"bc\")")]
    [Arguments("sp.LastIndexOf('c')", "arrayLastIndexOf(sp, \"c\")")]
    [Arguments("sp.Contains('c')", "arrayContains(sp, \"c\")")]
    [Arguments("sp.Trim()", "sp.trim()")]
    [Arguments("sp.TrimStart()", "sp.trimStart()")]
    [Arguments("sp.TrimEnd()", "sp.trimEnd()")]
    [Arguments("Span<int> s = xs; s.Reverse()", "s.reverse()")]
    [Arguments("xs.Sort((x, y) => y - x)", "sortArray(xs, (x: number, y: number) => (y - x) | 0)")]
    [Arguments(
        "Span<int> s = xs; s.Sort((x, y) => y - x)",
        "sortArray(s, (x: number, y: number) => (y - x) | 0)"
    )]
    [Arguments("xs.Sort(Descending)", "sortArray(xs, Calls.descending)")]
    [Arguments("xs.Sort()", "sortArray(xs)")]
    [Arguments("xs.Sort((IComparer<int>?)null)", "sortArray(xs)")]
    [Arguments("Span<int> s = xs; s.Sort()", "sortArray(s)")]
    [Arguments(
        "object o = sp.ToString(); ((string)o).StartsWith(sp)",
        "castTo<string>(o, StringType).startsWith(sp)"
    )]
    [Arguments(
        "object o = sp.ToString(); (o as string)!.IndexOf(sp)",
        "arrayIndexOf(asType(o, StringType)!, sp)"
    )]
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
            .Contains("return arrayContains(pairs, [3, 4] as [number, number]);");
    }

    /// <summary>
    /// A tuple search the span rule reports is reported once, by the span
    /// rule: the tuple comparison is moot for a call that throws.
    /// </summary>
    [Test]
    [Arguments("pairs.Contains(t, same)", "Contains")]
    [Arguments("MemoryExtensions.Contains(pairs, t)", "Contains")]
    [Arguments("pairs.IndexOf(others)", "IndexOf")]
    public async Task ReportedTupleSearch_ReportsOnce(string call, string method)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Calling(
                "(int, int)[] pairs, (int, int)[] others, (int, int) t, "
                    + "IEqualityComparer<(int, int)> same",
                call
            )
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .StartsWith($"The span method 'MemoryExtensions.{method}'");
    }

    [Test]
    [Arguments("List<int> xs, int x", "xs.Contains(x)", "xs.contains(x)")]
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
    [Arguments("int[] xs", "xs.Sort(Comparer<int>.Default)")]
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
