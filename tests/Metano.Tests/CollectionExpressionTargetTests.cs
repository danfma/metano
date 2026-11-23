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
    [Test]
    public async Task UnmodeledTargets_ReportUnsupported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections;
            using System.Collections.Frozen;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Bag : IEnumerable<int>
            {
                private readonly List<int> _items = new();

                public void Add(int x) => _items.Add(x * 10);

                public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public static class Targets
            {
                public static ImmutableSortedSet<int> Sorted() => [3, 1];

                public static FrozenSet<int> Frozen() => [1];

                public static ImmutableQueue<int> Queue() => [1];

                public static Bag UserBag() => [1, 2];
            }
            """
        );

        var targets = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(targets).Count().IsEqualTo(4);
        await Assert.That(targets.Any(m => m.Contains("ImmutableSortedSet<int>"))).IsTrue();
        await Assert.That(targets.Any(m => m.Contains("FrozenSet<int>"))).IsTrue();
        await Assert.That(targets.Any(m => m.Contains("ImmutableQueue<int>"))).IsTrue();
        await Assert.That(targets.Any(m => m.Contains("'Bag'"))).IsTrue();
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
