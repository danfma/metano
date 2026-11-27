using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Conformance;

public sealed class HandlerCell<T>
{
    public HandlerCell(T value)
    {
        Value = value;
    }

    public T Value { get; }
}

/// <summary>
/// A handler that records each call it receives with the overload it bound to,
/// so a row shows the construction, the parts in order and the overload of
/// every hole (FR-112).
/// </summary>
[InterpolatedStringHandler]
public sealed class CallLog
{
    private readonly List<string> _calls = new();

    public CallLog(int literalLength, int formattedCount)
    {
        _calls.Add("new(" + literalLength + "," + formattedCount + ")");
    }

    public void AppendLiteral(string literal) => _calls.Add("literal:" + literal);

    public void AppendFormatted<T>(HandlerCell<T> cell) => _calls.Add("cell:" + cell.Value);

    public void AppendFormatted<T>(T value) => _calls.Add("value:" + Describe(value));

    public void AppendFormatted<T>(T value, string? format) =>
        _calls.Add("format:" + Describe(value) + ":" + format);

    public void AppendFormatted<T>(T value, int alignment) =>
        _calls.Add("align:" + Describe(value) + ":" + alignment);

    public void AppendFormatted<T>(T value, int alignment, string? format) =>
        _calls.Add("both:" + Describe(value) + ":" + alignment + ":" + format);

    public void AppendFormatted(CallLog inner) => _calls.Add("inner:[" + inner.Text() + "]");

    public string Text() => string.Join(",", _calls);

    private static string Describe<T>(T value) =>
        value is HandlerCell<int> ? "cell-object" : value?.ToString() ?? "null";
}

/// <summary>
/// Collects what a <see cref="Gate"/> lets through and passes itself and a
/// limit to the handler's constructor.
/// </summary>
public sealed class GateRecorder
{
    public List<string> Calls { get; } = new();

    public string Write(int limit, [InterpolatedStringHandlerArgument("", "limit")] Gate gate) =>
        string.Join(",", Calls);
}

/// <summary>
/// A handler whose appends return <c>bool</c>: once it has recorded
/// <c>limit</c> parts, it answers <c>false</c> and C# skips the remaining
/// parts and their hole expressions.
/// </summary>
[InterpolatedStringHandler]
public sealed class Gate
{
    private readonly GateRecorder _recorder;

    private readonly int _limit;

    public Gate(int literalLength, int formattedCount, GateRecorder recorder, int limit)
    {
        _recorder = recorder;
        _limit = limit;
        recorder.Calls.Add("new(" + literalLength + "," + formattedCount + "," + limit + ")");
    }

    public bool AppendLiteral(string literal) => Record("literal:" + literal);

    public bool AppendFormatted(object? value, int alignment = 0, string? format = null) =>
        Record("value:" + value + ":" + alignment + ":" + (format ?? "null"));

    private bool Record(string call)
    {
        _recorder.Calls.Add(call);

        return _recorder.Calls.Count <= _limit;
    }
}

public static class InterpolatedStringHandlerProbes
{
    private static int _counter;

    public static string TypedHoles()
    {
        var cell = new HandlerCell<int>(3);
        var n = 7;
        object boxed = new HandlerCell<int>(1);

        return Render($"a{cell}b{n}c{boxed}");
    }

    public static string AlignmentAndFormat()
    {
        var n = 42;

        return Render($"{n, 5}|{n:D3}|{n, -4:X}");
    }

    public static string StatementPosition()
    {
        var n = 5;
        CallLog log = $"x{n}y";

        return log.Text();
    }

    public static string PlusChain()
    {
        var n = 1;
        var cell = new HandlerCell<string>("c");

        return Render($"x{n}" + $"y{cell}");
    }

    public static string NestedHandler()
    {
        var n = 2;

        return Render($"o{(CallLog)$"i{n}"}");
    }

    public static string HoleEvaluatedOnce()
    {
        _counter = 0;
        var text = Render($"{Next()}-{Next()}");

        return text + "|" + _counter;
    }

    public static string BoolShortCircuit()
    {
        _counter = 0;
        var recorder = new GateRecorder();
        var limit = 2;
        var text = recorder.Write(limit, $"a{Next()}b{Next(), 3:D2}c");

        return text + "|" + _counter;
    }

    public static string BoolAllParts()
    {
        _counter = 0;
        var recorder = new GateRecorder();
        var limit = 10;
        var text = recorder.Write(limit, $"a{Next()}b{Next(), 3:D2}c");

        return text + "|" + _counter;
    }

    private static string Render(CallLog log) => log.Text();

    private static int Next() => ++_counter;
}
