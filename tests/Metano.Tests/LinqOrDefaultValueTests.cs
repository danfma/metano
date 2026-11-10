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
        using System.Numerics;

        [Transpile]
        public enum Color { None, Red }

        [Transpile]
        public enum Level { Low = 1, High = 2 }

        [Transpile, StringEnum]
        public enum Tone { Soft = 1, Loud = 2 }

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

                    public int FirstMatchOrDefault(List<int> xs) => xs.FirstOrDefault(x => x > 100, default);

                    public bool FirstOrNamedDefault(List<bool> xs) => xs.FirstOrDefault(defaultValue: default);

                    public int FalsyMatch(List<int> xs) => xs.FirstOrDefault(x => x == 0, 42);

                    public int Last(int[] xs) => xs.LastOrDefault();

                    public int LastMatchOr(IEnumerable<int> xs) => xs.LastOrDefault(x => x > 100, 42);

                    public int LastNamed(List<int> xs) => xs.LastOrDefault(defaultValue: 42, predicate: x => x > 100);

                    public int Single(List<int> xs) => xs.SingleOrDefault(x => x > 100);

                    public int SingleOr(List<int> xs) => xs.SingleOrDefault(42);

                    public int Chained(List<int> xs) => xs.Where(x => x > 1).Select(x => x * 2).FirstOrDefault();

                    public long Wide(List<long> xs) => xs.FirstOrDefault(x => x > 100) + xs.Last();

                    public ulong Unsigned(List<ulong> xs) => xs.FirstOrDefault() + 3UL;

                    public nint Native(List<nint> xs) => xs.FirstOrDefault();

                    public nuint NativeUnsigned(List<nuint> xs) => xs.LastOrDefault();

                    public BigInteger Big(List<BigInteger> xs) => xs.SingleOrDefault();

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

                    public string? NameOrFallback(List<string> xs, string? fallback) => xs.FirstOrDefault(fallback);

                    public string? LastOrFallback(List<string> xs, string? fallback) => xs.LastOrDefault(x => x != "a", fallback);

                    public Person? Who(List<Person> xs) => xs.LastOrDefault(p => p.Name == "ana");

                    public T? Any<T>(List<T> xs) where T : class => xs.FirstOrDefault();
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("linq-or-default-values.ts", result["lookups.ts"]);
    }

    /// <summary>
    /// A queryable stage keeps its expression tree in the third slot, after
    /// the default value, which is <c>undefined</c> when the stage has none.
    /// </summary>
    [Test]
    public async Task QueryableStages_KeepTheTreeAfterTheDefaultValue()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + """

                [Transpile]
                public class Queries
                {
                    public Person? Queried(IQueryable<Person> xs) => xs.FirstOrDefault(p => p.Name == "ana");

                    public int QueriedCount(IQueryable<int> xs) => xs.FirstOrDefault(x => x > 100);
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("linq-or-default-queryable.ts", result["queries.ts"]);
    }

    /// <summary>
    /// <c>List&lt;T&gt;.Find</c> coalesces a miss to the same default.
    /// </summary>
    [Test]
    public async Task ListFind_CoalescesToTheTypeDefault()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + """

                [Transpile]
                public class Finders
                {
                    public int Found(List<int> xs) => xs.Find(x => x > 100);

                    public long FoundWide(List<long> xs) => xs.Find(x => x > 100) + 3;

                    public Color FoundZero(List<Color> xs) => xs.Find(c => c != Color.Red);

                    public Level FoundNoZero(List<Level> xs) => xs.Find(l => l != Level.Low);

                    public Person? FoundWho(List<Person> xs) => xs.Find(p => p.Name == "ana");

                    public int? FoundIn(List<int>? xs) => xs?.Find(x => x > 100);
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("linq-or-default-find.ts", result["finders.ts"]);
    }

    /// <summary>
    /// An element whose <c>default(T)</c> no literal spells (a user struct, a
    /// tuple, a <c>struct</c>-constrained type parameter, a
    /// <c>[StringEnum]</c> without a zero member) keeps the <c>null</c> miss,
    /// and each call reports <c>MS0001</c> as a warning, on the call. The
    /// <c>defaultValue</c> overloads are the way to choose the value.
    /// </summary>
    [Test]
    public async Task ElementsWithoutALiteralDefault_KeepNullAndWarn()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + """

                [Transpile]
                public class Zeroed
                {
                    public Point? At(List<Point> xs) => xs.FirstOrDefault();

                    public Point? AtDefault(List<Point> xs) => xs.FirstOrDefault(p => p.X > 1, default);

                    public (int A, int B)? Pair(List<(int A, int B)> xs) => xs.LastOrDefault(x => x.A > 1);

                    public T? Slot<T>(List<T> xs) where T : struct => xs.SingleOrDefault();

                    public Tone? Voice(List<Tone> xs) => xs.FirstOrDefault();

                    public Point? Found(List<Point> xs) => xs.Find(p => p.X > 1);

                    public Tone? FoundVoice(List<Tone> xs) => xs.Find(t => t != Tone.Loud);
                }
                """
        );

        var reported = diagnostics
            .Where(d => d.Severity != MetanoDiagnosticSeverity.Info)
            .Select(d => $"{d.Severity} {d.Code} at '{CallText(d)}': {d.Message}")
            .ToList();

        await Assert
            .That(reported)
            .IsEquivalentTo([
                Unrepresentable("xs.FirstOrDefault()", "Point", "FirstOrDefault"),
                Unrepresentable(
                    "xs.FirstOrDefault(p => p.X > 1, default)",
                    "Point",
                    "FirstOrDefault"
                ),
                Unrepresentable(
                    "xs.LastOrDefault(x => x.A > 1)",
                    "(int A, int B)",
                    "LastOrDefault"
                ),
                Unrepresentable("xs.SingleOrDefault()", "T", "SingleOrDefault"),
                Unrepresentable("xs.FirstOrDefault()", "Tone", "FirstOrDefault"),
                Unrepresentable("xs.Find(p => p.X > 1)", "Point", "Find"),
                Unrepresentable("xs.Find(t => t != Tone.Loud)", "Tone", "Find"),
            ]);

        await TranspileHelper.AssertGolden(
            "linq-or-default-unrepresentable.ts",
            result["zeroed.ts"]
        );
    }

    /// <summary>
    /// The zero member of an enum another package declares is imported from
    /// that package as a value.
    /// </summary>
    [Test]
    public async Task CrossPackageEnumElements_ImportTheZeroMember()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            [assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@acme/domain")]

            namespace Acme.Domain;

            public enum Stage { Draft, Final }
            """,
            """
            using System.Collections.Generic;
            using System.Linq;
            using Acme.Domain;
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("app")]

            namespace App;

            public class Pipeline
            {
                public Stage Current(List<Stage> xs) => xs.LastOrDefault();
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "linq-or-default-cross-package.ts",
            result["app/pipeline.ts"]
        );
    }

    private static string CallText(MetanoDiagnostic diagnostic) =>
        diagnostic.Location is { SourceTree: { } tree } location
            ? tree.GetText().ToString(location.SourceSpan)
            : throw new InvalidOperationException(
                $"The diagnostic '{diagnostic.Message}' has no source location."
            );

    private static string Unrepresentable(string call, string type, string lookup) =>
        $"Warning {DiagnosticCodes.UnsupportedFeature} at '{call}': 'default({type})' has no "
        + $"literal in the target: when no element qualifies, '{lookup}' returns null instead of "
        + "the default value.";
}
