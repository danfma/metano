using System;

namespace SampleRuntimeTypes.Shapes;

[Flags]
public enum Color
{
    None = 0,
    Red = 1,
    Green = 2,
    Blue = 4,
}

/// <summary>The <c>System.Enum</c> helpers over a descriptor (FR-024).</summary>
public static class Palette
{
    public static bool IsDefined(int value) => Enum.IsDefined(typeof(Color), value);

    public static Color Parse(string text) => Enum.Parse<Color>(text);

    public static Color ParseIgnoringCase(string text) => Enum.Parse<Color>(text, true);

    public static object FromRaw(int raw) => Enum.ToObject(typeof(Color), raw);
}
