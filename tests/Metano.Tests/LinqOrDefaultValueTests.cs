using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// An <c>OrDefault</c> LINQ terminal and <c>List&lt;T&gt;.Find</c> return
/// <c>default(T)</c> when no element qualifies (#601): the zero of a
/// non-nullable value type, <c>null</c> for a reference or
/// <c>Nullable&lt;T&gt;</c> element, or the <c>defaultValue</c> argument of
/// the .NET 6 overloads. The TypeScript terminals take that value as their
/// second argument, after the predicate. Both goldens compile under
/// <c>tsgo --strict --noImplicitOverride</c>.
/// </summary>
public class LinqOrDefaultValueTests
{
    private const string Types = """
        using System;
        using System.Collections.Generic;
        using System.Linq;

        [Transpile]
        public enum Color { None, Red }

        [Transpile]
        public enum Level { Low = 1, High = 2 }

        [Transpile]
        public readonly record struct Point(int X, int Y);

        [Transpile]
        public class Person(string name)
        {
            public string Name { get; } = name;
        }
        """;

    [Test]
    public async Task ValueElements_ReturnTheTypeDefaultOnAMiss()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + """

                [Transpile]
                public class Lookups
                {
                    public int First(List<int> xs) => xs.FirstOrDefault();

                    public int FirstMatch(List<int> xs) => xs.FirstOrDefault(x => x > 100);

                    public int FirstOr(List<int> xs) => xs.FirstOrDefault(42);

                    public int FirstMatchOr(List<int> xs) => xs.FirstOrDefault(x => x > 100, 42);

                    public int Last(int[] xs) => xs.LastOrDefault();

                    public int LastMatchOr(IEnumerable<int> xs) => xs.LastOrDefault(x => x > 100, 42);

                    public int LastNamed(List<int> xs) => xs.LastOrDefault(defaultValue: 42, predicate: x => x > 100);

                    public int Single(List<int> xs) => xs.SingleOrDefault(x => x > 100);

                    public int SingleOr(List<int> xs) => xs.SingleOrDefault(42);

                    public int Chained(List<int> xs) => xs.Where(x => x > 1).Select(x => x * 2).FirstOrDefault();

                    public long Wide(List<long> xs) => xs.FirstOrDefault(x => x > 100) + xs.Last();

                    public ulong Unsigned(List<ulong> xs) => xs.FirstOrDefault() + 3UL;

                    public bool Flag(List<bool> xs) => xs.FirstOrDefault();

                    public double Ratio(List<double> xs) => xs.FirstOrDefault();

                    public decimal Price(List<decimal> xs) => xs.FirstOrDefault();

                    public char Letter(List<char> xs) => xs.FirstOrDefault();

                    public Guid Id(List<Guid> xs) => xs.FirstOrDefault();

                    public Color Zero(List<Color> xs) => xs.FirstOrDefault();

                    public Level NoZero(List<Level> xs) => xs.FirstOrDefault();

                    public Point PointOr(List<Point> xs) => xs.FirstOrDefault(new Point(1, 2));

                    public int? Maybe(List<int?> xs) => xs.FirstOrDefault();

                    public int? MaybeOr(List<int?> xs) => xs.FirstOrDefault(42);

                    public string? Name(List<string> xs) => xs.FirstOrDefault();

                    public string NameOr(List<string> xs) => xs.FirstOrDefault("none");

                    public Person? Who(List<Person> xs) => xs.LastOrDefault(p => p.Name == "ana");

                    public T? Any<T>(List<T> xs) where T : class => xs.FirstOrDefault();

                    public Person? Queried(IQueryable<Person> xs) => xs.FirstOrDefault(p => p.Name == "ana");

                    public int QueriedCount(IQueryable<int> xs) => xs.FirstOrDefault(x => x > 100);

                    public int Found(List<int> xs) => xs.Find(x => x > 100);

                    public long FoundWide(List<long> xs) => xs.Find(x => x > 100) + 3;

                    public Person? FoundWho(List<Person> xs) => xs.Find(p => p.Name == "ana");

                    public int? FoundIn(List<int>? xs) => xs?.Find(x => x > 100);
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("linq-or-default-values.ts", result["lookups.ts"]);
    }

    /// <summary>
    /// A struct without a literal default (a user struct, a tuple, a
    /// <c>struct</c>-constrained type parameter) keeps the <c>null</c> miss,
    /// and each call reports <c>MS0001</c> as a warning. The
    /// <c>defaultValue</c> overloads are the way to choose the value.
    /// </summary>
    [Test]
    public async Task StructElementsWithoutALiteralDefault_KeepNullAndWarn()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + """

                [Transpile]
                public class Zeroed
                {
                    public Point? At(List<Point> xs) => xs.FirstOrDefault();

                    public (int A, int B)? Pair(List<(int A, int B)> xs) => xs.LastOrDefault(x => x.A > 1);

                    public T? Slot<T>(List<T> xs) where T : struct => xs.SingleOrDefault();

                    public Point? Found(List<Point> xs) => xs.Find(p => p.X > 1);
                }
                """
        );

        var reported = diagnostics
            .Where(d => d.Severity != MetanoDiagnosticSeverity.Info)
            .Select(d => $"{d.Severity} {d.Code}: {d.Message}")
            .ToList();

        await Assert
            .That(reported)
            .IsEquivalentTo([
                Unrepresentable("Point", "FirstOrDefault"),
                Unrepresentable("(int A, int B)", "LastOrDefault"),
                Unrepresentable("T", "SingleOrDefault"),
                Unrepresentable("Point", "Find"),
            ]);

        await TranspileHelper.AssertGolden(
            "linq-or-default-unrepresentable.ts",
            result["zeroed.ts"]
        );
    }

    private static string Unrepresentable(string type, string lookup) =>
        $"Warning {DiagnosticCodes.UnsupportedFeature}: 'default({type})' has no literal in the "
        + $"target: when no element qualifies, '{lookup}' returns null instead of the zeroed "
        + "struct.";
}
