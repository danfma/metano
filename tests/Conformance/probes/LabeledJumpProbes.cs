namespace Conformance;

/// <summary>Labeled loops and switches with C# 15 labeled break/continue (#772).</summary>
public static class LabeledJumpProbes
{
    public static string WhileContinueAndBreak()
    {
        int[] a = [1, 2, 3];
        var n = 0;
        var turns = 0;

        top:
        while (true)
        {
            turns++;

            while (n < a.Length)
            {
                n++;
                continue top;
            }

            break top;
        }

        return $"{n} {turns}";
    }

    public static string NestedForAndList()
    {
        var grid = new List<List<int>>
        {
            new() { 1, -1, 5 },
            new() { 0, 2, 12 },
            new() { 7 },
        };
        var log = "";

        rows:
        for (var row = 0; row < grid.Count; row++)
        {
            cols:
            foreach (var cell in grid[row])
            {
                if (cell < 0)
                    continue rows;

                if (cell == 0)
                    continue cols;

                if (cell > 9)
                {
                    log += $"break@{row}";
                    break rows;
                }

                log += cell + ",";
            }
        }

        return log;
    }

    public static string StringAndDoWhile()
    {
        var log = "";
        var n = 3;

        outer:
        do
        {
            chars:
            foreach (var c in "ab-cd")
            {
                if (c == '-')
                    continue outer;

                if (c == 'd')
                    break chars;

                log += c;
            }

            log += "|";
        } while (--n > 0);

        return log + n;
    }

    public static string SwitchBreak()
    {
        var log = "";

        foreach (var kind in new[] { 1, 2 })
        {
            dispatch:
            switch (kind)
            {
                case 1:
                    foreach (var step in new[] { 3, -1, 4 })
                    {
                        if (step < 0)
                            break dispatch;

                        log += step;
                    }

                    log += "!";
                    break;
                default:
                    log += "d";
                    break;
            }
        }

        return log;
    }

    private static bool TryNext(int[] a, int i, out int v)
    {
        v = i < a.Length ? a[i] : 0;

        return i < a.Length;
    }

    public static string OutVarWhileAndDo()
    {
        int[] a = [1, -2, 3, 500, 7];
        var i = 0;
        var whileTotal = 0;

        outer:
        while (TryNext(a, i++, out var v))
        {
            for (var k = 0; k < 2; k++)
            {
                if (v < 0)
                    continue outer;

                if (v > 100)
                    break outer;

                whileTotal += v;
            }
        }

        var j = 0;
        var doTotal = 0;

        loop:
        do
        {
            for (var k = 0; k < 2; k++)
            {
                if (a[j] > 100)
                    break loop;

                doTotal += a[j];
            }

            j++;
        } while (TryNext(a, j, out var w) && w != 7);

        return $"{whileTotal} {doTotal} {i} {j}";
    }
}
