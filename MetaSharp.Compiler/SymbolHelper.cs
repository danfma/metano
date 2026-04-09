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

    public static bool HasNoEmit(ISymbol symbol) => HasAttribute(symbol, "NoEmit");

    public static bool HasModuleEntryPoint(ISymbol symbol) => HasAttribute(symbol, "ModuleEntryPoint");

    /// <summary>
    /// Reads the file name from <c>[EmitInFile("name")]</c> on a type symbol, or null
    /// when the attribute isn't present (in which case the type takes its own name as
    /// the file).
    /// </summary>
    public static string? GetEmitInFile(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name is "EmitInFileAttribute" or "EmitInFile");
        if (attr is null || attr.ConstructorArguments.Length == 0) return null;
        return attr.ConstructorArguments[0].Value as string;
    }

    /// <summary>
    /// Reads <c>[ExportVarFromBody("name", AsDefault = ?, InPlace = ?)]</c> from a method
    /// symbol. Returns null when the attribute isn't present.
    /// </summary>
    public static ExportVarFromBodyInfo? GetExportVarFromBody(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name is "ExportVarFromBodyAttribute" or "ExportVarFromBody");
        if (attr is null) return null;

        var name = attr.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value?.ToString()
            : null;
        if (name is null) return null;

        var asDefault = false;
        var inPlace = false;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "AsDefault" && named.Value.Value is bool ad) asDefault = ad;
            else if (named.Key == "InPlace" && named.Value.Value is bool ip) inPlace = ip;
        }

        return new ExportVarFromBodyInfo(name, asDefault, inPlace);
    }

    public sealed record ExportVarFromBodyInfo(string Name, bool AsDefault, bool InPlace);

    /// <summary>
    /// Reads the <c>[assembly: EmitPackage("name", target)]</c> declaration from
    /// <paramref name="assembly"/> for the requested <paramref name="target"/>. Returns
    /// the package info (name + optional version override) on a match, or <c>null</c>
    /// when no matching attribute exists. Multiple <c>[EmitPackage]</c> instances are
    /// supported (one per target); the first one whose <c>Target</c> matches wins.
    /// </summary>
    /// <param name="targetEnumValue">Integer value of the EmitTarget enum (matches the
    /// underlying value the attribute was constructed with). Pass 0 for JavaScript.</param>
    public static EmitPackageInfo? GetEmitPackageInfo(IAssemblySymbol assembly, int targetEnumValue)
    {
        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("EmitPackageAttribute" or "EmitPackage"))
                continue;

            // Constructor: (string name, EmitTarget target = JavaScript)
            if (attr.ConstructorArguments.Length == 0) continue;
            var name = attr.ConstructorArguments[0].Value as string;
            if (string.IsNullOrEmpty(name)) continue;

            // Target arg may be omitted (default = JavaScript = 0) or present.
            var target = 0;
            if (attr.ConstructorArguments.Length > 1
                && attr.ConstructorArguments[1].Value is int t)
                target = t;
            if (target != targetEnumValue) continue;

            string? version = null;
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Version" && named.Value.Value is string v && v.Length > 0)
                    version = v;
            }

            return new EmitPackageInfo(name, version);
        }
        return null;
    }

    /// <summary>
    /// Convenience overload that returns just the package name (or null) for callers
    /// that don't care about the version override.
    /// </summary>
    public static string? GetEmitPackage(IAssemblySymbol assembly, int targetEnumValue) =>
        GetEmitPackageInfo(assembly, targetEnumValue)?.Name;

    public sealed record EmitPackageInfo(string Name, string? Version);

    public static bool HasInlineWrapper(ISymbol symbol) => HasAttribute(symbol, "InlineWrapper");

    /// <summary>
    /// Determines if a type should be transpiled, considering:
    /// 1. [NoTranspile] → always excluded
    /// 2. [NoEmit] → excluded from transpilation, but the type is still discoverable
    ///    via Roslyn semantic model so user code can reference it. The transpiler
    ///    won't generate a .ts file or import it from anywhere — it's an ambient
    ///    declaration over an external library shape.
    /// 3. [Transpile] → always included
    /// 4. assemblyWideTranspile + public → included
    /// </summary>
    public static bool IsTranspilable(ISymbol symbol, bool assemblyWideTranspile = false,
        IAssemblySymbol? currentAssembly = null)
    {
        if (HasNoTranspile(symbol)) return false;
        if (HasNoEmit(symbol)) return false;
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
    /// Reads <c>[Import("name", from: "module", AsDefault = ?, Version = ?)]</c> from
    /// a symbol.
    /// </summary>
    public static ImportInfo? GetImport(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name is "ImportAttribute" or "Import");

        if (attr is null) return null;

        var name = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value?.ToString() : null;
        var from = attr.ConstructorArguments.Length > 1 ? attr.ConstructorArguments[1].Value?.ToString() : null;

        if (name is null || from is null) return null;

        var asDefault = false;
        string? version = null;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "AsDefault" && named.Value.Value is bool ad) asDefault = ad;
            else if (named.Key == "Version" && named.Value.Value is string v && v.Length > 0)
                version = v;
        }

        return new ImportInfo(name, from, asDefault, version);
    }

    public sealed record ImportInfo(string Name, string From, bool AsDefault = false, string? Version = null);

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
