namespace Metano.Tests;

/// <summary>
/// A stage of a LINQ chain binds its arguments to the parameters of the
/// method C# resolved (#632): named arguments written in another order reach
/// their own parameters, and C#'s evaluation order is kept as for any other
/// call (spec 013 FR-017, NFR-004, #597). The golden compiles under
/// <c>tsgo --strict --noImplicitOverride</c>.
/// </summary>
public class LinqStageNamedArgumentTests
{
    private const string Host = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Metano.Annotations;

        [Transpile]
        public class Stages
        {
            private int _count;

            public int Seed => Next();

            public Func<int, int, int> Fn => Adder();

            public int Next()
            {
                _count = _count + 1;

                return _count;
            }

            public Func<int, int, int> Adder() => (a, x) => a + x;

            public Func<int, bool> Matcher() => x => x > 1;

            public Func<int, int> Picker() => x => x * 10;

            public List<int> Others() => new List<int> { 10, 20 };

            public Task<int> NextAsync() => Task.FromResult(Next());

            public Task<List<int>> Load() => Task.FromResult(Others());

        """;

    private const string BoundStages =
        Host
        + """
                public int Aggregate(List<int> xs) => xs.Aggregate(func: (a, x) => a + x, seed: 0);

                public Dictionary<int, int> ToDictionary(List<int> xs) =>
                    xs.ToDictionary(elementSelector: x => x * 10, keySelector: x => x);

                public List<int> Zip(List<int> xs, List<int> ys) =>
                    xs.Zip(resultSelector: (a, b) => a + b, second: ys).ToList();

                public int FirstOrDefault(List<int> xs) =>
                    xs.FirstOrDefault(defaultValue: 42, predicate: x => x > 1);

                public int OneEffect(List<int> xs) => xs.Aggregate(func: (a, x) => a + x, seed: Next());

                public int InOrder(List<int> xs) => xs.Aggregate(seed: Next(), func: Adder());

                public Dictionary<int, int> PositionalThenNamed(List<int> xs) =>
                    xs.ToDictionary(x => x, elementSelector: Picker());

                public int TwoEffects(List<int> xs) => xs.Aggregate(func: Adder(), seed: Next());

                public int ComputedReads(List<int> xs) => xs.Aggregate(func: Fn, seed: Seed);

                public List<int> MidPipe(List<int> xs) =>
                    xs.Where(x => x > 0)
                        .Zip(resultSelector: Adder(), second: Others())
                        .Select(x => x + 1)
                        .ToList();

                public int First(List<int> xs) =>
                    xs.FirstOrDefault(defaultValue: Next(), predicate: Matcher());

                public int Last(List<int> xs) =>
                    xs.LastOrDefault(defaultValue: Next(), predicate: Matcher());

                public int Single(List<int> xs) =>
                    xs.SingleOrDefault(defaultValue: Next(), predicate: Matcher());

                public int DefaultOnly(List<int> xs) => xs.FirstOrDefault(defaultValue: Next());

                public int? NullConditional(List<int>? xs) => xs?.Aggregate(func: Adder(), seed: Next());

                public async Task<int> AfterAwait() =>
                    (await Load()).Aggregate(func: Adder(), seed: await NextAsync());

                public List<int> QueryableZip(IQueryable<int> q) =>
                    q.Zip(resultSelector: (a, b) => a + b, source2: Others()).ToList();

                public int QueryableFirst(IQueryable<int> q) =>
                    q.FirstOrDefault(defaultValue: Next(), predicate: x => x > 1);

                public Dictionary<int, int> QueryableToDictionary(IQueryable<int> q) =>
                    q.ToDictionary(elementSelector: x => x * 10, keySelector: x => x);
            }
            """;

    /// <summary>
    /// Every stage passes its arguments in parameter order. A stage stays a
    /// plain call when its named arguments are in parameter order or at most
    /// one of them is order-sensitive; with two or more it binds them in
    /// source order, in its own position of the pipe. A queryable stage
    /// captures each lambda for its own parameter and keeps its tree last.
    /// </summary>
    [Test]
    public async Task NamedArguments_ReachTheirParametersInSourceOrder()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(BoundStages);

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code)).IsEmpty();
        await TranspileHelper.AssertGolden("linq-stage-named-arguments.ts", files["stages.ts"]);
    }

    /// <summary>
    /// Arguments named in parameter order, or out of it with nothing whose
    /// order can be observed, emit the text of the positional call.
    /// </summary>
    [Test]
    [Arguments("xs.Aggregate(seed: Next(), func: Adder())", "xs.Aggregate(Next(), Adder())")]
    [Arguments("xs.Aggregate(Next(), func: Adder())", "xs.Aggregate(Next(), Adder())")]
    [Arguments("xs.Aggregate(func: Adder(), seed: 7)", "xs.Aggregate(7, Adder())")]
    [Arguments(
        "xs.ToDictionary(elementSelector: x => x * 10, keySelector: x => x)",
        "xs.ToDictionary(x => x, x => x * 10)"
    )]
    [Arguments(
        "xs.FirstOrDefault(defaultValue: Next(), predicate: x => x > 1)",
        "xs.FirstOrDefault(x => x > 1, Next())"
    )]
    [Arguments(
        "xs.Zip(resultSelector: (a, b) => a + b, second: Others())",
        "xs.Zip(Others(), (a, b) => a + b)"
    )]
    public async Task UnobservableOrder_EmitsThePositionalCall(string named, string positional)
    {
        var expected = TranspileHelper.Transpile(Body(positional));
        var actual = TranspileHelper.Transpile(Body(named));

        await Assert.That(actual["stages.ts"]).IsEqualTo(expected["stages.ts"]);
    }

    /// <summary>
    /// A queryable stage with arguments bound in source order keeps its tree
    /// after them, inside the binding: it is no argument of the call.
    /// </summary>
    [Test]
    public async Task QueryableStage_KeepsItsTreeAfterTheBoundArguments()
    {
        var files = TranspileHelper.Transpile(
            Host
                + """
                    public IEqualityComparer<int> Comparer() => EqualityComparer<int>.Default;

                    public Dictionary<int, int> Run(IQueryable<int> q) =>
                        q.ToDictionary(comparer: Comparer(), elementSelector: Picker(), keySelector: x => x);
                }
                """
        );

        await Assert
            .That(files["stages.ts"])
            .Contains("(($arg2, $arg1, $arg0) => toMap($arg0, $arg1, $arg2, {\n      tree: {");

        await Assert
            .That(files["stages.ts"])
            .Contains("}))(this.comparer(), this.picker(), (x: number) => x));");
    }

    /// <summary>
    /// The Dart target does not lower LINQ chains (#549), so a reordered
    /// stage reports nothing about its evaluation order there.
    /// </summary>
    [Test]
    public async Task Dart_ReportsNoEvaluationOrder()
    {
        var reordered = Body("xs.Aggregate(func: Adder(), seed: Next())");
        var inOrder = Body("xs.Aggregate(seed: Next(), func: Adder())");

        var (_, reorderedDiagnostics) = TranspileHelper.TranspileDart(reordered);
        var (_, inOrderDiagnostics) = TranspileHelper.TranspileDart(inOrder);

        await Assert
            .That(reorderedDiagnostics.Select(diagnostic => diagnostic.Message))
            .IsEquivalentTo(inOrderDiagnostics.Select(diagnostic => diagnostic.Message));
    }

    private static string Body(string expression) =>
        Host
        + $$"""
                public object Run(List<int> xs) => {{expression}};
            }
            """;
}
