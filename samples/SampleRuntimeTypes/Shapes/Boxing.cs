using System;
using System.Collections.Generic;

namespace SampleRuntimeTypes.Shapes;

/// <summary>
/// Boxing of an enum at a C# boxing conversion (spec 011 FR-050, US3-9): the
/// value carries its descriptor, so <c>GetType()</c>, <c>ToString()</c> and
/// <c>Equals</c> answer as in .NET; a generic instantiation stays raw.
/// </summary>
public static class Boxing
{
    public static object BoxRed() => Color.Red;

    public static Type TypeOf(object value) => value.GetType();

    public static string TextOf(object value) => value.ToString() ?? "";

    public static bool IsRed(object value) => value.Equals(Color.Red);

    public static bool IsColor(object value) => value is Color;

    public static List<Color> Colors() => [Color.Red, Color.Green];
}
