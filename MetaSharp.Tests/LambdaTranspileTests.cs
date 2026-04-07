namespace MetaSharp.Tests;

public class LambdaTranspileTests
{
    [Test]
    public async Task SimpleLambda_ExpressionBody()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile, ExportedAsModule]
            public static class Filters
            {
                public static bool HasPositive(List<int> items)
                {
                    return items.Any(x => x > 0);
                }
            }
            """
        );

        var output = result["filters.ts"];
        await Assert.That(output).Contains("=>");
        await Assert.That(output).Contains("x > 0");
        await Assert.That(output).DoesNotContain("unsupported");
    }

    [Test]
    public async Task SimpleLambda_WithMemberAccess()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Linq;

            namespace App
            {
                [Transpile]
                public record Item(string Name, bool Active);

                [Transpile, ExportedAsModule]
                public static class ItemFilter
                {
                    public static int CountActive(List<Item> items)
                    {
                        return items.Count(i => i.Active);
                    }
                }
            }
            """
        );

        var output = result["item-filter.ts"];
        await Assert.That(output).Contains("=>");
        await Assert.That(output).Contains("i.active");
    }

    [Test]
    public async Task SimpleLambda_WithNegation()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile, ExportedAsModule]
            public static class Logic
            {
                public static bool NoneNegative(List<int> items)
                {
                    return items.All(x => !(x < 0));
                }
            }
            """
        );

        var output = result["logic.ts"];
        await Assert.That(output).Contains("=>");
        await Assert.That(output).DoesNotContain("unsupported");
    }

    [Test]
    public async Task ParenthesizedLambda_TwoParams()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile, ExportedAsModule]
            public static class MathUtils
            {
                public static int Apply(Func<int, int, int> fn, int a, int b)
                {
                    return fn(a, b);
                }

                public static int Sum(int a, int b)
                {
                    return Apply((x, y) => x + y, a, b);
                }
            }
            """
        );

        var output = result["math-utils.ts"];
        await Assert.That(output).Contains("=>");
        await Assert.That(output).Contains("x + y");
    }

    [Test]
    public async Task SimpleLambda_ProjectionWithSelect()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile, ExportedAsModule]
            public static class Projection
            {
                public static List<string> GetNames(List<string> items)
                {
                    return items.Select(s => s.ToUpper()).ToList();
                }
            }
            """
        );

        var output = result["projection.ts"];
        await Assert.That(output).Contains("=>");
        await Assert.That(output).Contains(".toUpperCase()");
    }

    [Test]
    public async Task Lambda_NoUnsupportedComment()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile, ExportedAsModule]
            public static class Check
            {
                public static bool HasAny(List<int> items)
                {
                    return items.Any(x => x > 0);
                }
            }
            """
        );

        var output = result["check.ts"];
        await Assert.That(output).DoesNotContain("unsupported");
        await Assert.That(output).DoesNotContain("SimpleLambdaExpression");
    }
}
