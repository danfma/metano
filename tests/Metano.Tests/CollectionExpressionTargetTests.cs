using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A collection expression over a type whose construction the transpiler
/// does not model (a collection builder such as <c>ImmutableSortedSet</c> or
/// <c>FrozenSet</c>, or a user type's <c>Add</c>) reports <c>MS0001</c>
/// instead of standing in a plain array for it (#581). The targets a literal
/// does stand for keep their lowering.
/// </summary>
public class CollectionExpressionTargetTests
{
    private const string UnmodeledSource = """
        using System.Collections;
        using System.Collections.Concurrent;
        using System.Collections.Frozen;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Collections.ObjectModel;
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        public class Bag : IEnumerable<int>
        {
            private readonly List<int> _items = new();

            public void Add(int x) => _items.Add(x * 10);

            public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public class MyList : List<int> { }

        public class MyColl : Collection<int> { }

        public class Hiding : List<int>
        {
            public new void Add(int x) => base.Add(x + 100);
        }

        public static class Targets
        {
            public static MyList Inherited() => [1, 2];

            public static MyColl InheritedColl() => [1, 2];

            public static Hiding Hidden() => [1, 2];

            public static Collection<int> Kept() => [1, 2];

            public static ObservableCollection<int> Observed() => [1, 2];

            public static ImmutableQueue<int> Queue() => [1, 2, 3];

            public static Collection<int> Empty() => [];

            public static ConcurrentBag<int> OneBag() => [1];

            public static ImmutableSortedSet<int> Sorted() => [3, 1];

            public static ImmutableStack<int> Stack() => [1, 2];

            public static FrozenSet<int> Frozen() => [1, 2];

            public static ConcurrentBag<int> TwoBag() => [1, 2];

            public static Bag UserBag() => [1, 2];
        }
        """;

    /// <summary>
    /// The array gives what .NET observes for an order-preserving list-like
    /// (and for a literal too short to reorder), so it stays, with the warning.
    /// </summary>
    [Test]
    [Arguments("static kept(): Collection<number> {\n    return [1, 2];")]
    [Arguments("static observed(): ObservableCollection<number> {\n    return [1, 2];")]
    [Arguments("static queue(): ImmutableQueue<number> {\n    return [1, 2, 3];")]
    [Arguments("static empty(): Collection<number> {\n    return [];")]
    [Arguments("static oneBag(): ConcurrentBag<number> {\n    return [1];")]
    [Arguments("static inherited(): MyList {\n    return [1, 2];")]
    [Arguments("static inheritedColl(): MyColl {\n    return [1, 2];")]
    public async Task OrderPreservingTargets_KeepTheArray(string expected)
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(UnmodeledSource);

        await Assert.That(files["targets.ts"]).Contains(expected);
    }

    /// <summary>
    /// A type that sorts, deduplicates, reverses or runs a user Add throws
    /// instead of standing a wrong array in for it.
    /// </summary>
    [Test]
    [Arguments("sorted")]
    [Arguments("stack")]
    [Arguments("frozen")]
    [Arguments("twoBag")]
    [Arguments("userBag")]
    [Arguments("hidden")]
    public async Task RebuildingTargets_Throw(string method)
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(UnmodeledSource);
        var output = files["targets.ts"];
        var body = output[output.IndexOf($"static {method}(", StringComparison.Ordinal)..];

        await Assert
            .That(body[..body.IndexOf("\n  }", StringComparison.Ordinal)])
            .Contains("throw new Error(\"Unsupported expression: CollectionExpression\")");
    }

    [Test]
    public async Task UnmodeledTargets_ReportWhatIsEmitted()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(UnmodeledSource);

        var messages = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(messages).Count().IsEqualTo(13);
        await Assert.That(messages.Count(m => m.Contains("a plain array stands in"))).IsEqualTo(7);
        await Assert.That(messages.Count(m => m.Contains("throws at run time"))).IsEqualTo(6);
    }

    [Test]
    public async Task ModeledTargets_KeepTheirLowering()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Targets
            {
                public static int Spans()
                {
                    Span<int> s = [1, 2];
                    ReadOnlySpan<int> r = [3];

                    return s[0] + r[0];
                }

                public static IEnumerable<int> Sequence() => [1];

                public static int[] Array() => [1];

                public static List<int> List() => [1];

                public static ImmutableArray<int> Immutable() => [1];

                public static IImmutableSet<int> Set() => [1];
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert.That(files["targets.ts"]).Contains("return ImmutableArray.of<number>([1]);");
        await Assert.That(files["targets.ts"]).Contains("return ImmutableHashSet.of<number>([1]);");
    }
}
