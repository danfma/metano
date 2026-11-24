using System.Text;

namespace Conformance;

/// <summary>
/// Patterns that read their subject more than once (#735): the subject runs once, only when
/// reached, and an element read of a string or array gives the same answer when it is repeated.
/// </summary>
public static class PatternSubjectProbes
{
    public static string ElementReadsOfStringAndArray()
    {
        var text = "a5-";
        var chars = new[] { '7', 'x' };
        var result = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
            result.Append(text[i] is >= '0' and <= '9' ? 'D' : '.');

        for (var i = 0; i < chars.Length; i++)
            result.Append(chars[i] is >= '0' and <= '9' ? 'D' : '.');

        return result.ToString();
    }

    public static string SubjectRunsOnce()
    {
        var calls = 0;
        Func<char> next = () =>
        {
            calls++;
            return '5';
        };

        var matched = next() is >= '0' and <= '9';

        return matched + ":" + calls;
    }

    public static string ShortCircuitSkipsTheSubject()
    {
        var calls = 0;
        Func<char> next = () =>
        {
            calls++;
            return '5';
        };

        var skipped = false && next() is >= '0' and <= '9';
        var reached = true && next() is >= '0' and <= '9';

        return skipped + ":" + reached + ":" + calls;
    }

    public static string SubjectInAnIfCondition()
    {
        var calls = 0;
        Func<string> next = () =>
        {
            calls++;
            return "b";
        };

        if (next() is "a" or "b")
            return "hit:" + calls;

        return "miss:" + calls;
    }

    public static string SubjectInAnExpressionLambda()
    {
        var calls = 0;
        Func<Func<char>, bool> test = next => next() is >= '0' and <= '9';

        var first = test(() =>
        {
            calls++;
            return '3';
        });

        var second = test(() =>
        {
            calls++;
            return 'z';
        });

        return first + ":" + second + ":" + calls;
    }

    public static string SubjectInARecursiveLambda()
    {
        var calls = 0;
        Func<int, int>? walk = null;

        walk = n =>
        {
            calls++;

            return n == 0 ? 0
                : walk!(n - 1) is >= 0 and <= 9 ? walk(n - 1) + 1
                : -1;
        };

        var result = walk(4);

        return result + ":" + calls;
    }

    public static string SubjectOfTwoPatternsInOneCondition()
    {
        var calls = 0;
        Func<int> next = () => ++calls;
        var both = next() is >= 1 and <= 1 && next() is >= 2 and <= 2;

        return both + ":" + calls;
    }

    public static string SubjectInACatchFilter()
    {
        var counter = new PsCounter();

        try
        {
            throw new InvalidOperationException("x");
        }
        catch (InvalidOperationException) when (counter.NextInt() is >= 1 and <= 100)
        {
            return "caught:" + counter.Calls;
        }
    }

    public static string ElementReadThroughAGetter()
    {
        var holder = new PsTextHolder("a5");
        var digits = 0;

        for (var i = 0; i < 2; i++)
        {
            if (holder.Text[i] is >= '0' and <= '9')
                digits++;
        }

        return digits + ":" + holder.Reads;
    }
}

public sealed class PsCounter
{
    public int Calls { get; private set; }

    public int NextInt() => ++Calls;
}

public sealed class PsTextHolder(string text)
{
    public int Reads { get; private set; }

    public string Text
    {
        get
        {
            Reads++;

            return text;
        }
    }
}
