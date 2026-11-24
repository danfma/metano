using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The types TypeScript cannot print at a default-text site (baseline FR-094.4):
/// a <c>[PlainObject]</c> type has no class, so it has no <c>toString()</c> and
/// prints <c>[object Object]</c> where C# prints its text.
/// </summary>
internal static class TsUnprintableText
{
    /// <summary>The reason <paramref name="type"/> does not print, or <c>null</c> when it does.</summary>
    public static string? ReasonOf(INamedTypeSymbol type) =>
        type.HasPlainObject()
            ? $"a [PlainObject] '{type.Name}' has no class and so no ToString() in TypeScript: "
                + "it prints '[object Object]' instead of its C# text."
            : null;
}
