using Metano.Interop;

namespace SampleRuntimeTypes.Interop;

/// <summary>
/// User story 6 of spec 011: one-off TypeScript glue through <c>Js.Code</c> (the
/// text is verbatim, the holes are transpiled C#), and the compile-time escape
/// hatches <c>UnsafeCast&lt;T&gt;()</c> and <c>AsDynamic()</c>.
/// </summary>
public static class InlineCode
{
    /// <summary>A typed expression: <c>(performance.now()) as number</c>.</summary>
    public static double Now() => Js.Code<double>("performance.now()");

    /// <summary>A hole is an operand: <c>((a + b).toFixed(2)) as string</c>.</summary>
    public static string Sum(double a, double b) => Js.Code<string>($"{a + b}.toFixed(2)");

    /// <summary>A hole spliced into a JavaScript call.</summary>
    public static string Shout(string text) => Js.Code<string>($"{text}.toUpperCase()");

    /// <summary>Statement text: writes a global the C# side does not model.</summary>
    public static void Remember(string value)
    {
        Js.Code($"(globalThis as any).__sampleRemembered = {value}");
    }

    /// <summary>An untyped read: <c>(…) as any</c>.</summary>
    public static object? Remembered() => Js.Code("(globalThis as any).__sampleRemembered ?? null");

    /// <summary>Parses JSON into a known shape, without a check.</summary>
    public static PointDto ParsePoint(string json) =>
        Js.Code<object>($"JSON.parse({json})").UnsafeCast<PointDto>();

    /// <summary>The same value, seen as <c>dynamic</c>.</summary>
    public static object Loose(PointDto point) => point.AsDynamic();
}
