using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A LINQ call the runtime operators do not implement reports MS0001 and
/// lowers to the throwing placeholder (#648) instead of running another
/// overload, ignoring a comparer, misplacing a queryable tree or becoming a
/// member call on the receiver (<c>xs.join(…)</c> reaching
/// <c>Array.prototype.join</c>). Every overload the runtime implements emits
/// what it emitted before (<c>linq-supported-overloads.ts</c>, which compiles
/// under <c>tsgo --strict --noImplicitOverride</c>).
/// </summary>
public class LinqUnimplementedOverloadTests
{
    private const string Host = """
        using System.Collections.Generic;
        using Metano.Annotations;
        using System.Linq;

        [Transpile]
        public class Calls
        {
        """;

    [Test]
    [Arguments(
        "xs.Aggregate(100, (a, x) => a + x, r => r * 2)",
        "The overload 'Aggregate(seed, func, resultSelector)' is not supported; the runtime implements only 'Aggregate(seed, func)'. The call throws at run time."
    )]
    [Arguments(
        "xs.Aggregate((a, x) => a + x)",
        "The overload 'Aggregate(func)' is not supported; the runtime implements only 'Aggregate(seed, func)'. The call throws at run time."
    )]
    [Arguments(
        "xs.GroupBy(x => x % 2, x => x * 10)",
        "The overload 'GroupBy(keySelector, elementSelector)' is not supported; the runtime implements only 'GroupBy(keySelector)'. The call throws at run time."
    )]
    [Arguments(
        "xs.GroupBy(x => x % 2, (key, items) => key)",
        "The overload 'GroupBy(keySelector, resultSelector)' is not supported; the runtime implements only 'GroupBy(keySelector)'. The call throws at run time."
    )]
    [Arguments(
        "xs.GroupBy(x => x % 2, x => x, same)",
        "The overload 'GroupBy(keySelector, elementSelector, comparer)' is not supported; the runtime implements only 'GroupBy(keySelector)'. The call throws at run time."
    )]
    [Arguments(
        "xs.Zip(ys)",
        "The overload 'Zip(second)' is not supported; the runtime implements only 'Zip(second, resultSelector)'. The call throws at run time."
    )]
    [Arguments(
        "xs.Zip(ys, ys)",
        "The overload 'Zip(second, third)' is not supported; the runtime implements only 'Zip(second, resultSelector)'. The call throws at run time."
    )]
    [Arguments(
        "xs.SelectMany(x => ys, (x, y) => x + y)",
        "The overload 'SelectMany(collectionSelector, resultSelector)' is not supported; the runtime implements only 'SelectMany(selector)'. The call throws at run time."
    )]
    [Arguments(
        "xs.Take(1..2)",
        "The overload 'Take(range)' is not supported; the runtime implements only 'Take(count)'. The call throws at run time."
    )]
    [Arguments(
        "map.ToDictionary()",
        "The overload 'ToDictionary()' is not supported; the runtime implements only 'ToDictionary(keySelector)' and 'ToDictionary(keySelector, elementSelector)'. The call throws at run time."
    )]
    public async Task UnimplementedOverload_ReportsAtTheCall(string call, string message)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert.That(report.Message).IsEqualTo(message);
        await Assert.That(report.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(LocatedText(report)).IsEqualTo(call);
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnsupportedLinqOverload"));
    }

    [Test]
    [Arguments("xs.OrderBy(x => x, order)", "OrderBy", "order")]
    [Arguments("xs.OrderByDescending(x => x, order)", "OrderByDescending", "order")]
    [Arguments("xs.OrderBy(x => x).ThenBy(x => -x, order)", "ThenBy", "order")]
    [Arguments("xs.OrderBy(x => x).ThenByDescending(x => -x, order)", "ThenByDescending", "order")]
    [Arguments("xs.GroupBy(x => x % 2, same)", "GroupBy", "same")]
    [Arguments("xs.ToDictionary(x => x, same)", "ToDictionary", "same")]
    [Arguments("xs.ToDictionary(x => x, x => x * 2, same)", "ToDictionary", "same")]
    [Arguments("xs.ToHashSet(same)", "ToHashSet", "same")]
    [Arguments("xs.Contains(3, same)", "Contains", "same")]
    [Arguments("xs.Distinct(same)", "Distinct", "same")]
    [Arguments("xs.DistinctBy(x => x % 3, same)", "DistinctBy", "same")]
    [Arguments("xs.MinBy(x => x, order)", "MinBy", "order")]
    [Arguments("xs.MaxBy(x => x, order)", "MaxBy", "order")]
    [Arguments("xs.Min(order)", "Min", "order")]
    [Arguments("xs.Max(order)", "Max", "order")]
    [Arguments("xs.OrderBy(comparer: order, keySelector: x => x)", "OrderBy", "order")]
    [Arguments(
        "xs.Distinct(EqualityComparer<int>.Default)",
        "Distinct",
        "EqualityComparer<int>.Default"
    )]
    public async Task IgnoredComparer_ReportsAtTheComparer(
        string call,
        string method,
        string comparer
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                $"The comparer of '{method}' is not supported; the default comparison would be "
                    + "used. The call throws at run time."
            );

        await Assert.That(LocatedText(report)).IsEqualTo(comparer);
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnsupportedLinqOverload"));
    }

    [Test]
    [Arguments("xs.Join(ys, o => o, i => i, (o, i) => o + i)", "Join")]
    [Arguments("xs.GroupJoin(ys, o => o, i => i, (o, i) => o)", "GroupJoin")]
    [Arguments("xs.ToLookup(x => x % 2)", "ToLookup")]
    [Arguments("xs.SequenceEqual(ys)", "SequenceEqual")]
    [Arguments("xs.ElementAt(1)", "ElementAt")]
    [Arguments("xs.Cast<object>()", "Cast")]
    public async Task UnloweredLinqMethod_ReportsInsteadOfAMemberCall(string call, string method)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                $"The LINQ method '{method}' has no runtime counterpart; the call throws at run time."
            );

        await Assert.That(LocatedText(report)).IsEqualTo(call);
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnloweredLinqCall"));

        await Assert
            .That(files["calls.ts"])
            .DoesNotContain($"xs.{char.ToLowerInvariant(method[0])}");
    }

    /// <summary>
    /// An unlowered method inside a chain is the source of the stages after
    /// it, which still lower.
    /// </summary>
    [Test]
    public async Task UnloweredLinqMethod_InsideAChain_ReportsAtItsCall()
    {
        var call = "xs.Join(ys, o => o, i => i, (o, i) => o + i)";

        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body($"{call}.Select(x => x * 2)")
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert.That(LocatedText(report)).IsEqualTo(call);

        await Assert
            .That(files["calls.ts"])
            .Contains(
                "return linq((() => {\n      throw new Error(\"Unsupported expression: UnloweredLinqCall\");\n    })(), select((x: number) => x * 2));"
            );
    }

    /// <summary>
    /// Every unimplemented stage of a chain reports, in source order; the
    /// whole chain lowers to the placeholder.
    /// </summary>
    [Test]
    public async Task SeveralUnimplementedStages_ReportEach()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("xs.GroupBy(x => x % 2, x => x).OrderBy(g => g.Key, order).Count()")
        );

        await Assert
            .That(string.Join(" | ", UnsupportedFeatures(diagnostics).Select(LocatedText)))
            .IsEqualTo("xs.GroupBy(x => x % 2, x => x) | order");

        await Assert
            .That(files["calls.ts"])
            .Contains(
                "return (() => {\n      throw new Error(\"Unsupported expression: UnsupportedLinqOverload\");"
            );
    }

    [Test]
    [Arguments(
        "q.ToDictionary(x => x)",
        "The expression tree of the queryable 'ToDictionary(keySelector)' has no slot in the runtime operator 'toMap'. The call throws at run time."
    )]
    [Arguments(
        "q.Aggregate(0, (a, x) => a + x)",
        "The expression tree of the queryable 'Aggregate(seed, func)' has no slot in the runtime operator 'aggregate'. The call throws at run time."
    )]
    public async Task MisplacedQueryableTree_ReportsAtTheCall(string call, string message)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Host
                + $$"""
                    public object Run(IQueryable<int> q) => {{call}};
                }
                """
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert.That(report.Message).IsEqualTo(message);
        await Assert.That(LocatedText(report)).IsEqualTo(call);
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnsupportedLinqOverload"));
    }

    /// <summary>
    /// The static form (<c>Enumerable.Aggregate(xs, …)</c>, #619) of an
    /// unimplemented overload reports the same diagnostic.
    /// </summary>
    [Test]
    public async Task StaticForm_UnimplementedOverload_Reports()
    {
        var call = "Enumerable.Aggregate(xs, 100, (a, x) => a + x, r => r * 2)";
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                "The overload 'Aggregate(seed, func, resultSelector)' is not supported; the "
                    + "runtime implements only 'Aggregate(seed, func)'. The call throws at run time."
            );

        await Assert.That(LocatedText(report)).IsEqualTo(call);
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnsupportedLinqOverload"));
    }

    /// <summary>
    /// A stage the chain walker leaves to the plain call, because a stage
    /// inside it is in the static form, still reports its comparer.
    /// </summary>
    [Test]
    public async Task StageOverAStaticFormStage_ReportsItsComparer()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("Enumerable.Where(xs, x => x > 0).Distinct(same)")
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert.That(LocatedText(report)).IsEqualTo("same");
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnsupportedLinqOverload"));
    }

    /// <summary>
    /// Instance methods and static helpers named like a LINQ method, and a
    /// LINQ method a declarative mapping lowers, are not LINQ calls nothing
    /// lowers.
    /// </summary>
    [Test]
    [Arguments("xs.Contains(3)", "xs.includes(3)")]
    [Arguments("string.Join(\",\", xs)", "xs.join(\",\")")]
    [Arguments("xs.Find(x => x > 1)", "xs.find((x: number) => x > 1)")]
    [Arguments("xs.Union(ys)", ".union(ys)")]
    public async Task OtherMappings_DoNotReport(string call, string emitted)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains(emitted);
    }

    /// <summary>
    /// The Dart target does not lower LINQ chains (#549): nothing changes
    /// there.
    /// </summary>
    [Test]
    [Arguments("xs.GroupBy(x => x % 2, x => x * 10)")]
    [Arguments("xs.OrderBy(x => x, order)")]
    [Arguments("xs.Join(ys, o => o, i => i, (o, i) => o + i)")]
    [Arguments("xs.AsQueryable().ToDictionary(x => x)")]
    [Arguments("Enumerable.Aggregate(xs, 100, (a, x) => a + x, r => r * 2)")]
    public async Task Dart_ReportsNothing(string call)
    {
        var (_, diagnostics) = TranspileHelper.TranspileDart(Body(call));

        await Assert
            .That(
                diagnostics.Where(diagnostic => diagnostic.Message.Contains("throws at run time"))
            )
            .IsEmpty();
    }

    [Test]
    public async Task SupportedOverloads_EmitAsBefore()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public class Supported
            {
                public object Where(List<int> xs) => xs.Where(x => x > 1).Where((x, i) => i > 0);

                public object Select(List<int> xs) => xs.Select(x => x * 2).Select((x, i) => x + i);

                public object SelectMany(List<List<int>> xss) => xss.SelectMany(x => x);

                public object Ascending(List<int> xs) => xs.OrderBy(x => x).ThenBy(x => -x);

                public object Descending(List<int> xs) => xs.OrderByDescending(x => x).ThenByDescending(x => -x);

                public object GroupBy(List<int> xs) => xs.GroupBy(x => x % 2);

                public object Paging(List<int> xs) => xs.Skip(1).Take(2).SkipWhile(x => x < 1).TakeWhile(x => x < 5);

                public object Distinct(List<int> xs) => xs.Distinct().DistinctBy(x => x % 3);

                public object Concat(List<int> xs, List<int> ys) => xs.Concat(ys).Append(4).Prepend(0).Reverse();

                public object Zip(List<int> xs, List<int> ys) => xs.Zip(ys, (a, b) => a + b);

                public int[] ToArray(List<int> xs) => xs.ToArray();

                public List<int> ToList(List<int> xs) => xs.ToList();

                public Dictionary<int, int> ToDictionary(List<int> xs) => xs.ToDictionary(x => x);

                public Dictionary<int, int> ToDictionaryElement(List<int> xs) => xs.ToDictionary(x => x, x => x * 2);

                public HashSet<int> ToHashSet(List<int> xs) => xs.ToHashSet();

                public int Firsts(List<int> xs) => xs.First() + xs.First(x => x > 1) + xs.Last() + xs.Last(x => x > 1) + xs.Single() + xs.Single(x => x > 1);

                public int OrDefaults(List<int> xs) =>
                    xs.FirstOrDefault() + xs.FirstOrDefault(x => x > 1) + xs.LastOrDefault(7) + xs.SingleOrDefault(x => x > 1, 7);

                public bool Quantifiers(List<int> xs) => xs.Any() && xs.Any(x => x > 1) && xs.All(x => x > 0);

                public long Counts(List<int> xs) => xs.Count() + xs.Count(x => x > 1) + xs.LongCount() + xs.LongCount(x => x > 1);

                public double Numbers(List<int> xs) =>
                    xs.Sum() + xs.Sum(x => x * 2) + xs.Min() + xs.Min(x => -x) + xs.Max() + xs.Max(x => -x) + xs.Average() + xs.Average(x => x * 2);

                public int Extremes(List<int> xs) => xs.MinBy(x => -x) + xs.MaxBy(x => -x);

                public bool Contains(IEnumerable<int> xs) => xs.Contains(3);

                public int Aggregate(List<int> xs) => xs.Aggregate(0, (a, x) => a + x);

                public object Queryable(IQueryable<int> q, List<int> ys) =>
                    q.Where(x => x > 1).OrderBy(x => x).Select(x => x * 2).Concat(ys).Zip(ys, (a, b) => a + b);

                public Dictionary<int, int> QueryableToDictionary(IQueryable<int> q) => q.ToDictionary(x => x, x => x * 2);

                public int QueryableTerminals(IQueryable<int> q) =>
                    q.First(x => x > 1) + q.FirstOrDefault(x => x > 1, 7) + q.Count(x => x > 1) + q.Sum(x => x) + (q.Contains(3) ? 1 : 0);
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("linq-supported-overloads.ts", result["supported.ts"]);
    }

    private static string Body(string expression) =>
        Host
        + $$"""
                public object Run(
                    List<int> xs,
                    List<int> ys,
                    IComparer<int> order,
                    IEqualityComparer<int> same,
                    Dictionary<int, int> map
                ) => {{expression}};
            }
            """;

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature);

    private static string LocatedText(MetanoDiagnostic diagnostic) =>
        diagnostic.Location!.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string Placeholder(string kind) =>
        $"throw new Error(\"Unsupported expression: {kind}\");";
}
