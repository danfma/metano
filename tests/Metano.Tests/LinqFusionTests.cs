namespace Metano.Tests;

/// <summary>
/// Build-time fusion of adjacent LINQ stages (#207). Each test pins one
/// rule of <c>IrLinqChainFuser</c> by transpiling a chain that hits the
/// rule and inspecting the emitted <c>linq(...)</c> call.
/// </summary>
public class LinqFusionTests
{
    [Test]
    public async Task WhereWhere_Fused_IntoAnd()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> ActiveAdults(IQueryable<User> users) =>
                    users.Where(u => u.Age >= 18).Where(u => u.Active);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
                public bool Active { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        // Only a single where() slot survives — the second Where folded
        // into the first via AND.
        var whereOccurrences = CountOccurrences(output, "where(");
        await Assert.That(whereOccurrences).IsEqualTo(1);
        await Assert.That(output).Contains("\"&&\"");
        await Assert.That(output).Contains("tree:");
    }

    [Test]
    public async Task WhereWhere_QueryableMeta_UnionsCaptures()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Filter(IQueryable<User> users, int minAge, bool flag)
                {
                    int threshold = minAge;
                    bool wanted = flag;
                    return users.Where(u => u.Age >= threshold).Where(u => u.Active == wanted);
                }
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
                public bool Active { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        var whereOccurrences = CountOccurrences(output, "where(");
        await Assert.That(whereOccurrences).IsEqualTo(1);
        // Both capture names land in the merged captures bundle.
        await Assert.That(output).Contains("threshold:");
        await Assert.That(output).Contains("wanted:");
    }

    [Test]
    public async Task SelectSelect_NotYetFused()
    {
        // Select+Select fusion is out of scope for this PR — the chain
        // emits two select() slots as it always has.
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<int> AgePlusOne(IEnumerable<User> users) =>
                    users.Select(u => u.Age).Select(a => a + 1);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        var selectOccurrences = CountOccurrences(output, "select(");
        await Assert.That(selectOccurrences).IsEqualTo(2);
    }

    [Test]
    public async Task TakeTake_LiteralArgs_FusedToMin()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> FirstFew(IEnumerable<User> users) =>
                    users.Take(10).Take(5);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        var takeOccurrences = CountOccurrences(output, "take(");
        await Assert.That(takeOccurrences).IsEqualTo(1);
        await Assert.That(output).Contains("take(5)");
    }

    [Test]
    public async Task TakeTake_NonLiteralArg_NotFused()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> FirstFew(IEnumerable<User> users, int n) =>
                    users.Take(n).Take(5);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        var takeOccurrences = CountOccurrences(output, "take(");
        await Assert.That(takeOccurrences).IsEqualTo(2);
    }

    [Test]
    public async Task SkipSkip_Literal_FusedToSum()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Rest(IEnumerable<User> users) =>
                    users.Skip(3).Skip(2);
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        var skipOccurrences = CountOccurrences(output, "skip(");
        await Assert.That(skipOccurrences).IsEqualTo(1);
        await Assert.That(output).Contains("skip(5)");
    }

    [Test]
    public async Task ReverseReverse_BothDropped()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Identity(IEnumerable<User> users) =>
                    users.Reverse().Reverse();
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        // Both reverse() stages vanish; the linq() call still wraps the
        // source because the chain originated as a LINQ chain.
        await Assert.That(output).DoesNotContain("reverse(");
    }

    [Test]
    public async Task ReverseTriple_CollapsesToSingleReverse()
    {
        var result = TranspileHelper.TranspileWithIrBodies(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public static class UserExt
            {
                public static IEnumerable<User> Flip(IEnumerable<User> users) =>
                    users.Reverse().Reverse().Reverse();
            }

            [Transpile]
            public class User
            {
                public int Age { get; set; }
            }
            """
        );

        var output = result["user-ext.ts"];
        var reverseOccurrences = CountOccurrences(output, "reverse(");
        await Assert.That(reverseOccurrences).IsEqualTo(1);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
            return 0;
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
