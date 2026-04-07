using Microsoft.CodeAnalysis;

namespace MetaSharp.Compiler;

/// <summary>
/// Target-agnostic helpers for reading MetaSharp attributes from Roslyn symbols and
/// performing common name conversions used by the file system layout (kebab-case).
///
/// Methods that are TypeScript/JavaScript-specific (camelCase identifiers, JS reserved
/// words, [Emit] string templates) live in the TypeScript target instead.
/// </summary>
public static class SymbolHelper
{
    public static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == attributeName
            || a.AttributeClass?.Name == attributeName + "Attribute");
    }

    public static string? GetNameOverride(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name is "NameAttribute" or "Name");

        if (attr is { ConstructorArguments.Length: > 0 })
            return attr.ConstructorArguments[0].Value?.ToString();

        return null;
    }

    public static bool HasTranspile(ISymbol symbol) => HasAttribute(symbol, "Transpile");

    public static bool HasStringEnum(ISymbol symbol) => HasAttribute(symbol, "StringEnum");

    public static bool HasFlags(ISymbol symbol) => HasAttribute(symbol, "Flags") || HasAttribute(symbol, "FlagsAttribute");

    public static bool HasIgnore(ISymbol symbol) => HasAttribute(symbol, "Ignore");

    public static bool HasModule(ISymbol symbol) => HasAttribute(symbol, "Module");

    public static bool HasExportedAsModule(ISymbol symbol) => HasAttribute(symbol, "ExportedAsModule");

    public static bool HasImport(ISymbol symbol) => HasAttribute(symbol, "Import");

    public static bool HasGenerateGuard(ISymbol symbol) => HasAttribute(symbol, "GenerateGuard");

    public static bool HasNoTranspile(ISymbol symbol) => HasAttribute(symbol, "NoTranspile");

    public static bool HasInlineWrapper(ISymbol symbol) => HasAttribute(symbol, "InlineWrapper");

    /// <summary>
    /// Determines if a type should be transpiled, considering:
    /// 1. [NoTranspile] → always excluded
    /// 2. [Transpile] → always included
    /// 3. assemblyWideTranspile + public → included
    /// </summary>
    public static bool IsTranspilable(ISymbol symbol, bool assemblyWideTranspile = false,
        IAssemblySymbol? currentAssembly = null)
    {
        if (HasNoTranspile(symbol)) return false;
        if (HasTranspile(symbol)) return true;
        // Assembly-wide: only for types in the current compilation's assembly (not BCL/referenced assemblies)
        if (assemblyWideTranspile
            && symbol.DeclaredAccessibility == Accessibility.Public
            && (currentAssembly is null
                || SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, currentAssembly)))
            return true;
        return false;
    }

    /// <summary>
    /// Reads [Import("name", from: "module")] from a symbol.
    /// </summary>
    public static (string Name, string From)? GetImport(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name is "ImportAttribute" or "Import");

        if (attr is null) return null;

        var name = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value?.ToString() : null;
        var from = attr.ConstructorArguments.Length > 1 ? attr.ConstructorArguments[1].Value?.ToString() : null;

        if (name is null || from is null) return null;
        return (name, from);
    }

    /// <summary>
    /// Converts PascalCase to kebab-case for file paths.
    /// Examples: "UserId" → "user-id", "InMemoryIssueRepository" → "in-memory-issue-repository",
    /// "IIssueRepository" → "i-issue-repository", "PageRequest" → "page-request".
    /// </summary>
    public static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                // Insert hyphen before any uppercase that follows a lowercase or digit,
                // OR before an uppercase that is followed by a lowercase (acronym boundary).
                var prev = name[i - 1];
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                var prevIsLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                var nextIsLower = char.IsLower(next);
                if (prevIsLowerOrDigit || (char.IsUpper(prev) && nextIsLower))
                    sb.Append('-');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
