namespace Metano.Compiler;

/// <summary>
/// Pure-string helpers for working with C# namespace paths. Lives in the
/// core so any frontend or backend that needs to reason about namespace
/// hierarchies (e.g., to compute an assembly's root namespace) can share
/// the same implementation.
/// </summary>
public static class NamespaceUtilities
{
    /// <summary>
    /// Longest dot-separated prefix shared by every entry in
    /// <paramref name="namespaces"/>. Returns the single entry verbatim
    /// when the list has one element, and the empty string when the list
    /// is empty or no segments overlap.
    /// </summary>
    public static string FindCommonPrefix(IReadOnlyList<string> namespaces)
    {
        if (namespaces.Count == 0)
            return "";
        if (namespaces.Count == 1)
            return namespaces[0];

        var parts = namespaces[0].Split('.');
        var commonLength = parts.Length;

        for (var i = 1; i < namespaces.Count; i++)
        {
            var otherParts = namespaces[i].Split('.');
            commonLength = System.Math.Min(commonLength, otherParts.Length);

            for (var j = 0; j < commonLength; j++)
            {
                if (parts[j] != otherParts[j])
                {
                    commonLength = j;
                    break;
                }
            }
        }

        return string.Join(".", parts.Take(commonLength));
    }
}
