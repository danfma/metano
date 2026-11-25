namespace Conformance;

/// <summary>
/// A switch expression outside a <c>return</c> (spec 001 FR-095, #735): in a local
/// initializer, an assignment, an argument and nested in another switch's arm, the subject runs
/// once, arms and their <c>when</c> guards run in order and stop at the first match, designators
/// are arm-local (a closure keeps the value its own evaluation bound), and an unmatched value
/// throws.
/// </summary>
public static class SwitchChainProbes
{
    static string _log = "";

    static SrShape Subject(SrShape shape)
    {
        _log += "s";

        return shape;
    }

    static bool Guard(string name, bool result)
    {
        _log += name;

        return result;
    }

    static string Echo(string text) => text;

    public static string InitializerOrder()
    {
        _log = "";

        var text = Subject(new SrSquare(3)) switch
        {
            SrCircle c when Guard("a", c.Radius > 0) => "circle",
            SrSquare s when Guard("b", s.Side > 5) => "big-square",
            SrSquare s when Guard("c", s.Side > 1) => "square:" + s.Side,
            _ when Guard("d", true) => "never",
            _ => "other",
        };

        return text + ":" + _log;
    }

    public static string AssignmentAndArgument()
    {
        _log = "";
        var text = "";
        text = Subject(new SrCircle(4)) switch
        {
            SrCircle { Radius: > 10 } => "big",
            SrCircle c when Guard("g", c.Radius > 3) => "mid:" + c.Radius,
            _ => "small",
        };

        var echoed = Echo(
            Subject(new SrCircle(1)) switch
            {
                SrSquare => "square",
                var other => "other:" + other,
            }
        );

        return text + "," + echoed + ":" + _log;
    }

    public static string NestedInArm()
    {
        var results = "";

        foreach (var n in new[] { 1, 7, 3 })
        {
            SrShape shape = n == 3 ? new SrSquare(n) : new SrCircle(n);
            var text =
                "<"
                + shape switch
                {
                    SrCircle c => n switch
                    {
                        1 => "one",
                        var k when k > c.Radius - 1 => "k" + k,
                        _ => "rest",
                    },
                    SrSquare s => "sq" + s.Side,
                    _ => "?",
                }
                + ">";
            results += text;
        }

        return results;
    }

    public static string ClosuresKeepTheirOwnBinding()
    {
        var readers = new List<Func<int>>();

        foreach (var n in new[] { 2, 5, 9 })
        {
            SrShape shape = new SrCircle(n);
            Func<int> reader = shape switch
            {
                SrCircle c => () => c.Radius,
                _ => () => -1,
            };
            readers.Add(reader);
        }

        return string.Join(",", readers.Select(reader => reader()));
    }

    public static string UnmatchedValueThrows()
    {
        var text = "";

        foreach (var value in new[] { 1, 2, 3 })
        {
            try
            {
                var name = value switch
                {
                    1 => "one",
                    2 => "two",
                };
                text += name + ",";
            }
            catch (InvalidOperationException exception)
            {
                text += exception.GetType().Name + ":" + exception.Message.Replace("\n", " ");
            }
        }

        return text;
    }

    public static string ThrowArm() => ThrowArmOf(4);

    static string ThrowArmOf(int value)
    {
        try
        {
            var name = Echo(
                value switch
                {
                    1 => "one",
                    _ => throw new ArgumentException("no " + value),
                }
            );

            return name;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    static int InSection(int selector, SrShape shape)
    {
        switch (selector)
        {
            case 1:
                var a =
                    1
                    + shape switch
                    {
                        SrCircle c => c.Radius,
                        _ => 0,
                    };
                return a;
            case 2:
                var b =
                    2
                    + shape switch
                    {
                        SrCircle c => c.Radius * 10,
                        _ => 0,
                    };
                return b;
            default:
                return -1;
        }
    }

    public static string TwoSwitchSections() =>
        InSection(1, new SrCircle(4))
        + ","
        + InSection(2, new SrCircle(4))
        + ","
        + InSection(2, new SrSquare(4))
        + ","
        + InSection(3, new SrCircle(4));

    public static string LoopHeaderClosures()
    {
        SrShape[] shapes = [new SrCircle(1), new SrCircle(2), new SrCircle(3)];
        var readers = new List<Func<int>>();
        var i = 0;
        Func<int>? reader;

        while (
            (
                reader = (i < shapes.Length ? shapes[i++] : null) switch
                {
                    SrCircle c => () => c.Radius,
                    _ => null,
                }
            ) != null
        )
            readers.Add(reader);

        return string.Join(",", readers.Select(r => r()));
    }

    public static string NullCoalescingThrowWithSwitch()
    {
        try
        {
            object? missing = null;
            SrShape shape = new SrCircle(6);
            var value = missing
                ?? throw new ArgumentException(
                    shape switch
                    {
                        SrCircle c => "circle " + c.Radius,
                        _ => "?",
                    }
                );

            return "" + value;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }
}
