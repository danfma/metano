namespace Metano.Tests;

public class ForEachTranspileTests
{
    [Test]
    public async Task ForEach_OverList_LowersToForOf()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App;

            [Transpile]
            public sealed class Printer
            {
                public void PrintAll(IReadOnlyList<string> lines)
                {
                    foreach (var line in lines)
                    {
                        System.Console.WriteLine(line);
                    }
                }
            }
            """
        );

        var output = result["app/printer.ts"];
        await Assert.That(output).Contains("for (const line of lines)");
        await Assert.That(output).Contains("console.log(line)");
    }

    [Test]
    public async Task ForEach_OverLinqChain_EvaluatesOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Linq;

            namespace App;

            [Transpile]
            public sealed class Filter
            {
                public void Run(IReadOnlyList<int> source)
                {
                    foreach (var x in source.Where(n => n > 0).Select(n => n * 2))
                    {
                        System.Console.WriteLine(x);
                    }
                }
            }
            """
        );

        var output = result["app/filter.ts"];
        await Assert.That(output).Contains("for (const x of");
        // After #20 the LINQ chain lowers to the pipe form: a single
        // `linq(source, where(...), select(...))` call instead of a
        // fluent `.where().select()` chain on `Enumerable.from(...)`.
        await Assert.That(output).Contains("linq(source, where(");
        await Assert.That(output).Contains("select(");
    }

    [Test]
    public async Task ForEach_BreakAndContinue_LowersDirectly()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App;

            [Transpile]
            public sealed class Walker
            {
                public void Walk(IReadOnlyList<int> nums)
                {
                    foreach (var n in nums)
                    {
                        if (n < 0) continue;
                        if (n > 100) break;
                        System.Console.WriteLine(n);
                    }
                }
            }
            """
        );

        var output = result["app/walker.ts"];
        await Assert.That(output).Contains("for (const n of nums)");
        await Assert.That(output).Contains("continue");
        await Assert.That(output).Contains("break");
    }

    [Test]
    public async Task ForEach_Nested_BothBindingsSurvive()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App;

            [Transpile]
            public sealed class Matrix
            {
                public void Print(IReadOnlyList<IReadOnlyList<int>> grid)
                {
                    foreach (var row in grid)
                    {
                        foreach (var cell in row)
                        {
                            System.Console.WriteLine(cell);
                        }
                    }
                }
            }
            """
        );

        var output = result["app/matrix.ts"];
        await Assert.That(output).Contains("for (const row of grid)");
        await Assert.That(output).Contains("for (const cell of row)");
    }

    [Test]
    public async Task ForEach_OverMethodCall_EvaluatesCallExpression()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            namespace App;

            [Transpile]
            public sealed class Repo
            {
                public IReadOnlyList<string> GetItems() => new List<string>();

                public void Run()
                {
                    foreach (var x in GetItems())
                    {
                        System.Console.WriteLine(x);
                    }
                }
            }
            """
        );

        var output = result["app/repo.ts"];
        await Assert.That(output).Contains("for (const x of this.getItems())");
    }
}
