/**
 * The default text of a C# `bool` (`$"{flag}"`, `flag.ToString()`, `"x" + flag`): .NET
 * prints `True` and `False`, where JavaScript writes `true` and `false`.
 */

/** `true` as `"True"`, `false` as `"False"`; a `null` value (a C# `bool?`) is the empty string. */
export function formatBoolean(value: boolean | null | undefined): string {
  if (value == null) {
    return "";
  }

  return value ? "True" : "False";
}
