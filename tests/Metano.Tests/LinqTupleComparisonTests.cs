using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A LINQ operator that compares C# tuples by value reports MS0001, as the collection lookups
/// do: the tuples lower to arrays the runtime compares by reference (#746).
/// </summary>
public class LinqTupleComparisonTests
{
    [Test]
    [Arguments("items.Distinct().Count()")]
    [Arguments("items.GroupBy(item => item).Count()")]
    [Arguments("items.Select(item => item).Distinct().Count()")]
    [Arguments("items.ToHashSet().Count")]
    [Arguments("items.ToDictionary(item => item).Count")]
    [Arguments("items.Except(items).Count()")]
    public async Task LinqOverTuples_ReportsTheReferenceComparison(string expression)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public class Queries
            {
                public object Run(List<(int, int)> items) => {{expression}};
            }
            """
        );

        var report = diagnostics.Single(diagnostic =>
            diagnostic.Code == DiagnosticCodes.UnsupportedFeature
            && diagnostic.Message.Contains("a LINQ operator")
        );

        await Assert.That(report.Message).Contains("compares C# tuples by value");
    }

    [Test]
    public async Task LinqOverTuples_WithAComparerReportsNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public class Queries
            {
                public int Run(List<(int, int)> items, IEqualityComparer<(int, int)> same) =>
                    items.Distinct(same).Count();
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }
}
