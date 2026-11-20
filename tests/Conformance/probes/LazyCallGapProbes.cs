using Metano.Annotations;

[assembly: MapMethod(typeof(Conformance.LzMappedCall), "Twice", JsTemplate = "($0 + $0)")]
[assembly: MapMethod(typeof(Conformance.LzMappedMethodCall), "Abs", JsMethod = "Math.abs")]
[assembly: MapMethod(
    typeof(Conformance.LzMappedFilteredCall),
    "Read",
    JsTemplate = "$0",
    WhenArg0StringEquals = "x"
)]
[assembly: MapMethod(typeof(Conformance.LzMappedFailure), "Read", JsTemplate = "$0")]

namespace Conformance;

public class LzInlineCall
{
    static LzInlineCall() => LazyLog.Write("init-inline");

    [Inline]
    public static int Twice(int value) => value + value;
}

public class LzInlineNamedCall
{
    static LzInlineNamedCall() => LazyLog.Write("init-inline-named");

    [Inline]
    public static int Named(int first, int second) => first * 10 + second;
}

public class LzSubstituteCall
{
    static LzSubstituteCall() => LazyLog.Write("init-substitute");

    [Inline(InlineMode.Substitute)]
    public static int Twice(int value) => value + value;
}

public class LzEmitCall
{
    static LzEmitCall() => LazyLog.Write("init-emit");

    [Emit("($0 + $0)")]
    public static int Twice(int value) => value + value;

    [Emit("7")]
    public static int Ignore(int value) => 7;
}

public class LzEmitNamedCall
{
    static LzEmitNamedCall() => LazyLog.Write("init-emit-named");

    [Emit("($0 * 10 + $1)")]
    public static int Named(int first, int second) => first * 10 + second;
}

public class LzMappedCall
{
    static LzMappedCall() => LazyLog.Write("init-mapped");

    public static int Twice(int value) => value + value;
}

public class LzMappedMethodCall
{
    static LzMappedMethodCall() => LazyLog.Write("init-mapped-method");

    public static int Abs(int value) => Math.Abs(value);
}

public class LzMappedFilteredCall
{
    static LzMappedFilteredCall() => LazyLog.Write("init-mapped-filtered");

    public static string Read(string value) => value;
}

public class LzInlineFailure
{
    static LzInlineFailure()
    {
        LazyLog.Write("init-fail");
        throw new InvalidOperationException("broken");
    }

    [Inline]
    public static int Read(int value) => value;
}

public class LzEmitFailure
{
    static LzEmitFailure()
    {
        LazyLog.Write("init-fail");
        throw new InvalidOperationException("broken");
    }

    [Emit("$0")]
    public static int Read(int value) => value;
}

public class LzMappedFailure
{
    static LzMappedFailure()
    {
        LazyLog.Write("init-fail");
        throw new InvalidOperationException("broken");
    }

    public static int Read(int value) => value;
}

public record LzNullEqual(int Value)
{
    static LzNullEqual() => LazyLog.Write("init-equal");
}

public record LzNullNotEqual(int Value)
{
    static LzNullNotEqual() => LazyLog.Write("init-not-equal");
}

public record LzRecordFailure(int Value)
{
    static LzRecordFailure()
    {
        LazyLog.Write("init-fail");
        throw new InvalidOperationException("broken");
    }
}

public record struct LzLiftedMissing
{
    public int Value { get; set; }

    static LzLiftedMissing() => LazyLog.Write("init-missing");
}

public record struct LzLiftedPresent
{
    public int Value { get; set; }

    static LzLiftedPresent() => LazyLog.Write("init-present");
}

public static class LazyCallGapProbes
{
    private static int Argument(string name, int value)
    {
        LazyLog.Write(name);
        return value;
    }

    public static string InlineFirst()
    {
        var value = LzInlineCall.Twice(Argument("arg", 3));
        var again = LzInlineCall.Twice(Argument("again", 4));
        return LazyLog.Take("result=" + value + ":" + again);
    }

    public static string InlineNamedOrder()
    {
        var value = LzInlineNamedCall.Named(
            second: Argument("second", 2),
            first: Argument("first", 1)
        );

        return LazyLog.Take("result=" + value);
    }

    public static string SubstituteFirst()
    {
        var value = LzSubstituteCall.Twice(Argument("arg", 3));
        return LazyLog.Take("result=" + value);
    }

    public static string EmitFirst()
    {
        var value = LzEmitCall.Twice(Argument("arg", 3));
        var ignored = LzEmitCall.Ignore(Argument("ignored", 4));
        return LazyLog.Take("result=" + value + ":" + ignored);
    }

    public static string EmitNamedOrder()
    {
        var value = LzEmitNamedCall.Named(
            second: Argument("second", 2),
            first: Argument("first", 1)
        );

        return LazyLog.Take("result=" + value);
    }

    public static string MappedFirst()
    {
        var value = LzMappedCall.Twice(Argument("arg", 3));
        return LazyLog.Take("result=" + value);
    }

    public static string MappedMethodFirst()
    {
        var value = LzMappedMethodCall.Abs(Argument("arg", -3));
        return LazyLog.Take("result=" + value);
    }

    public static string MappedFilteredFirst()
    {
        var value = LzMappedFilteredCall.Read("x");
        return LazyLog.Take("result=" + value);
    }

    public static string InlineFailureCached() =>
        Failure(() => LzInlineFailure.Read(Argument("arg", 1)));

    public static string EmitFailureCached() =>
        Failure(() => LzEmitFailure.Read(Argument("arg", 1)));

    public static string MappedFailureCached() =>
        Failure(() => LzMappedFailure.Read(Argument("arg", 1)));

    private static string Failure(Func<int> call)
    {
        TypeInitializationException? first = null;

        for (var i = 0; i < 2; i++)
        {
            try
            {
                call();
            }
            catch (TypeInitializationException error)
            {
                if (first == null)
                    first = error;
                else
                    LazyLog.Write("cached=" + ReferenceEquals(first, error));
            }
        }

        return LazyLog.Take("end");
    }

    public static string RecordNullEqual()
    {
        var equal = NullEqualArgument("left") == NullEqualArgument("right");
        LzNullEqual? left = null;
        var literal = left == null;
        return LazyLog.Take("result=" + equal + ":" + literal);
    }

    private static LzNullEqual? NullEqualArgument(string name)
    {
        LazyLog.Write(name);
        return null;
    }

    public static string RecordNullNotEqual()
    {
        LzNullNotEqual? left = null;
        var unequal = left != null;
        return LazyLog.Take("result=" + unequal);
    }

    public static string RecordFailureCached() =>
        Failure(() => (LzRecordFailure?)null == null ? 1 : 0);

    public static string LiftedRecordMissing()
    {
        LzLiftedMissing? left = default(LzLiftedMissing);
        LzLiftedMissing? right = null;
        var equal = left == right;
        var unequal = left != right;
        return LazyLog.Take("result=" + equal + ":" + unequal);
    }

    public static string LiftedRecordPresent()
    {
        LzLiftedPresent? left = default(LzLiftedPresent);
        LzLiftedPresent? right = default(LzLiftedPresent);
        var equal = left == right;
        return LazyLog.Take("result=" + equal);
    }
}
