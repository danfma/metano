using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A LINQ call the runtime operators do not implement reports MS0001 at the
/// method name and lowers to the throwing placeholder (#648) instead of
/// running another overload, ignoring a comparer, misplacing a queryable
/// tree or becoming a member call on the receiver (<c>xs.join(…)</c>
/// reaching <c>Array.prototype.join</c>). A <c>null</c> comparer lowers as
/// the comparer-less overload and a tree an operator reads nowhere is
/// dropped, as both mean what C# means. Every overload the runtime
/// implements emits what it emitted before (<c>linq-supported-overloads.ts</c>,
/// which compiles under <c>tsgo --strict --noImplicitOverride</c>).
/// </summary>
public class LinqUnimplementedOverloadTests
{
    /// <summary>
    /// The calls of the golden <c>linq-supported-overloads.ts</c>: one per
    /// overload the runtime signatures declare
    /// (<c>LinqRuntimeSignatureTests</c> checks that).
    /// </summary>
    internal const string SupportedSource = """
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

            public int FirstOrDefaults(List<int> xs) => xs.FirstOrDefault(7) + xs.FirstOrDefault(x => x > 1, 7);

            public int LastOrDefaults(List<int> xs) =>
                xs.LastOrDefault() + xs.LastOrDefault(x => x > 1) + xs.LastOrDefault(x => x > 1, 7);

            public int SingleOrDefaults(List<int> xs) => xs.SingleOrDefault() + xs.SingleOrDefault(x => x > 1) + xs.SingleOrDefault(7);

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
        """;

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
    public async Task UnimplementedOverload_ReportsAtTheMethodName(string call, string message)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert.That(report.Message).IsEqualTo(message);
        await Assert.That(report.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo(MethodNameOf(call));
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

        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo(comparer);
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnsupportedLinqOverload"));
    }

    /// <summary>
    /// A comparer that is the <c>null</c> constant asks for the default
    /// comparison, as the comparer-less overload does: the call lowers as
    /// that overload and reports nothing.
    /// </summary>
    [Test]
    [Arguments("xs.Distinct(null)", "xs.Distinct()")]
    [Arguments("xs.Distinct(default)", "xs.Distinct()")]
    [Arguments("xs.DistinctBy(x => x % 3, null)", "xs.DistinctBy(x => x % 3)")]
    [Arguments("xs.OrderBy(x => x, null)", "xs.OrderBy(x => x)")]
    [Arguments("xs.OrderByDescending(x => x, (null))", "xs.OrderByDescending(x => x)")]
    [Arguments(
        "xs.OrderBy(x => x).ThenBy(x => -x, (IComparer<int>?)null)",
        "xs.OrderBy(x => x).ThenBy(x => -x)"
    )]
    [Arguments("xs.GroupBy(x => x % 2, comparer: null)", "xs.GroupBy(x => x % 2)")]
    [Arguments("xs.ToDictionary(x => x, x => x * 2, null)", "xs.ToDictionary(x => x, x => x * 2)")]
    [Arguments("xs.ToHashSet(null)", "xs.ToHashSet()")]
    [Arguments("xs.MinBy(x => x, null)", "xs.MinBy(x => x)")]
    [Arguments("xs.Max(comparer: null)", "xs.Max()")]
    [Arguments("xs.OrderBy(comparer: null, keySelector: x => x)", "xs.OrderBy(x => x)")]
    public async Task NullComparer_LowersAsTheComparerLessOverload(string call, string comparerLess)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));
        var expected = TranspileHelper.Transpile(Body(comparerLess));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).IsEqualTo(expected["calls.ts"]);
    }

    /// <summary>
    /// <c>Contains</c> without a comparer binds <c>List&lt;T&gt;.Contains</c>,
    /// so its <c>null</c>-comparer call is compared with the pipe terminal.
    /// </summary>
    [Test]
    public async Task NullComparerOfContains_LowersAsTheContainsTerminal()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("xs.Contains(4, null)")
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains("return linq(xs, contains(4));");
    }

    [Test]
    [Arguments("xs.Join(ys, o => o, i => i, (o, i) => o + i)", "Join")]
    [Arguments("xs.GroupJoin(ys, o => o, i => i, (o, i) => o)", "GroupJoin")]
    [Arguments("xs.ToLookup(x => x % 2)", "ToLookup")]
    [Arguments("xs.SequenceEqual(ys)", "SequenceEqual")]
    [Arguments("xs.ElementAt(1)", "ElementAt")]
    [Arguments("xs.Cast<object>()", "Cast")]
    [Arguments("Enumerable.SequenceEqual(xs, ys)", "SequenceEqual")]
    [Arguments("Enumerable.ElementAt(xs, 1)", "ElementAt")]
    public async Task UnloweredLinqMethod_ReportsInsteadOfAMemberCall(string call, string method)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                $"The LINQ method '{method}' has no runtime counterpart; the call throws at run time."
            );

        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo(method);
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnloweredLinqCall"));

        await Assert
            .That(files["calls.ts"])
            .DoesNotContain($".{char.ToLowerInvariant(method[0])}{method[1..]}(");
    }

    /// <summary>
    /// An unlowered method inside a chain is the source of the stages after
    /// it, which still lower.
    /// </summary>
    [Test]
    public async Task UnloweredLinqMethod_InsideAChain_ReportsAtItsName()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("xs.Join(ys, o => o, i => i, (o, i) => o + i).Select(x => x * 2)")
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo("Join");

        await Assert
            .That(files["calls.ts"])
            .Contains(
                "return linq((() => {\n      throw new Error(\"Unsupported expression: UnloweredLinqCall\");\n    })(), select((x: number) => x * 2));"
            );
    }

    /// <summary>
    /// Every unimplemented stage of a chain reports, in source order and at
    /// its own position; the whole chain lowers to the placeholder.
    /// </summary>
    [Test]
    public async Task SeveralUnimplementedStages_ReportEach()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("xs.GroupBy(x => x % 2, x => x).Zip(ys).OrderBy(t => t.Second, order).Count()")
        );

        var reports = UnsupportedFeatures(diagnostics).ToList();

        await Assert
            .That(string.Join(" | ", reports.Select(LocatedSource.TextOf)))
            .IsEqualTo("GroupBy | Zip | order");

        await Assert
            .That(reports.Select(report => report.Location!.SourceSpan.Start).Distinct().Count())
            .IsEqualTo(3);

        await Assert
            .That(files["calls.ts"])
            .Contains(
                "return (() => {\n      throw new Error(\"Unsupported expression: UnsupportedLinqOverload\");"
            );
    }

    /// <summary>
    /// A queryable <c>ToDictionary</c> with one selector would hand its tree
    /// to the value-selector slot of <c>toMap</c>.
    /// </summary>
    [Test]
    public async Task MisplacedQueryableTree_ReportsAtTheMethodName()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Queryable("q.ToDictionary(x => x)")
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                "The expression tree of the queryable 'ToDictionary(keySelector)' has no slot in "
                    + "the runtime operator 'toMap'. The call throws at run time."
            );

        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo("ToDictionary");
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnsupportedLinqOverload"));
    }

    /// <summary>
    /// <c>aggregate</c> reads no tree, so a queryable <c>Aggregate</c> drops
    /// it and runs the plain operator.
    /// </summary>
    [Test]
    public async Task QueryableAggregate_DropsTheTree()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Queryable("q.Where(x => x > 2).Aggregate(0, (a, x) => a + x)")
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();

        await Assert
            .That(files["calls.ts"])
            .Contains("}), aggregate(0, (a: number, x: number) => a + x));");
    }

    /// <summary>
    /// A supported queryable stage whose named arguments are written out of
    /// parameter order (#632) binds each to its parameter and keeps the tree
    /// of its predicate after them. No supported stage has two
    /// order-sensitive arguments beside a lambda, so the tree never follows
    /// arguments bound in source order.
    /// </summary>
    [Test]
    public async Task QueryableStage_WithReorderedNamedArguments_KeepsTheTreeOfItsLambda()
    {
        var files = TranspileHelper.Transpile(
            Host
                + """
                    public int Next() => 7;

                    public int Run(IQueryable<int> q) =>
                        q.FirstOrDefault(defaultValue: Next(), predicate: x => x > 1);
                }
                """
        );

        await Assert
            .That(files["calls.ts"])
            .Contains(
                "return linq(q, firstOrDefault((x: number) => x > 1, this.next(), {\n      tree: {\n        kind: \"binary\",\n        op: \">\","
            );
    }

    /// <summary>
    /// The static form (<c>Enumerable.Aggregate(xs, …)</c>, #619) of an
    /// unimplemented overload reports the same diagnostic.
    /// </summary>
    [Test]
    public async Task StaticForm_UnimplementedOverload_Reports()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("Enumerable.Aggregate(xs, 100, (a, x) => a + x, r => r * 2)")
        );

        var report = UnsupportedFeatures(diagnostics).Single();

        await Assert
            .That(report.Message)
            .IsEqualTo(
                "The overload 'Aggregate(seed, func, resultSelector)' is not supported; the "
                    + "runtime implements only 'Aggregate(seed, func)'. The call throws at run time."
            );

        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo("Aggregate");
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

        await Assert.That(LocatedSource.TextOf(report)).IsEqualTo("same");
        await Assert.That(files["calls.ts"]).Contains(Placeholder("UnsupportedLinqOverload"));
    }

    /// <summary>
    /// A declarative mapping lowers a LINQ method only for the overloads its
    /// filter matches, as the BCL mapper applies it: a <c>WhenArgCount</c>
    /// that excludes the call leaves the call unlowered.
    /// </summary>
    [Test]
    [Arguments(7, true)]
    [Arguments(1, false)]
    public async Task DeclarativeMapping_CoversOnlyTheOverloadsItsFilterMatches(
        int whenArgCount,
        bool reports
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Metano.Annotations;

            [assembly: MapMethod(typeof(Enumerable), "TakeLast", WhenArgCount = {{whenArgCount}}, JsMethod = "takeLast")]

            [Transpile]
            public class Calls
            {
                public object Run(List<int> xs) => xs.TakeLast(2);
            }
            """
        );

        await Assert.That(UnsupportedFeatures(diagnostics).Any()).IsEqualTo(reports);
    }

    /// <summary>
    /// Instance methods and static helpers named like a LINQ method are not
    /// LINQ calls nothing lowers.
    /// </summary>
    [Test]
    [Arguments("xs.Contains(3)", "xs.includes(3)")]
    [Arguments("string.Join(\",\", xs)", "xs.join(\",\")")]
    [Arguments("xs.Find(x => x > 1)", "xs.find((x: number) => x > 1)")]
    public async Task OtherMappings_DoNotReport(string call, string emitted)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains(emitted);
    }

    /// <summary>
    /// <c>Union</c>, <c>Intersect</c> and <c>Except</c> have a declarative
    /// mapping, so the unlowered-method check does not report them. That
    /// legacy mapping is broken itself — it imports an <c>Enumerable</c> the
    /// runtime does not export (#657) — so its output is not pinned here.
    /// </summary>
    [Test]
    [Arguments("xs.Union(ys)")]
    [Arguments("xs.Intersect(ys)")]
    [Arguments("xs.Except(ys)")]
    public async Task LegacyMappedLinqMethod_IsNotReportedAsUnlowered(string call)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The Dart target does not lower LINQ chains (#549): nothing is reported
    /// there.
    /// </summary>
    [Test]
    [Arguments("xs.GroupBy(x => x % 2, x => x * 10)")]
    [Arguments("xs.OrderBy(x => x, order)")]
    [Arguments("xs.Join(ys, o => o, i => i, (o, i) => o + i)")]
    [Arguments("xs.AsQueryable().ToDictionary(x => x)")]
    [Arguments("Enumerable.Aggregate(xs, 100, (a, x) => a + x, r => r * 2)")]
    [Arguments("Enumerable.SequenceEqual(xs, ys)")]
    public async Task Dart_ReportsNothing(string call)
    {
        var (_, diagnostics) = TranspileHelper.TranspileDart(Body(call));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task SupportedOverloads_EmitAsBefore()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(SupportedSource);

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

    private static string Queryable(string expression) =>
        Host
        + $$"""
                public object Run(IQueryable<int> q) => {{expression}};
            }
            """;

    /// <summary>The name of the first method <paramref name="call"/> invokes.</summary>
    private static string MethodNameOf(string call)
    {
        var name = call[(call.IndexOf('.') + 1)..];

        return name[..name.IndexOf('(')];
    }

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature);

    private static string Placeholder(string kind) =>
        $"throw new Error(\"Unsupported expression: {kind}\");";
}
