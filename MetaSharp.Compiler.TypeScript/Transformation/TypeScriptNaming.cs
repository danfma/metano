using Microsoft.CodeAnalysis;
using MetaSharp.Compiler;

namespace MetaSharp.Transformation;

/// <summary>
/// TypeScript/JavaScript-specific naming and attribute helpers:
/// camelCase identifier conversion, JS reserved word escaping, and the
/// [Emit("$0.foo($1)")] string template attribute.
///
/// Target-agnostic helpers (kebab-case, attribute presence, [Transpile]/[Ignore]/etc.)
/// live in <see cref="MetaSharp.Compiler.SymbolHelper"/>.
/// </summary>
public static class TypeScriptNaming
{
    public static bool HasEmit(ISymbol symbol) => SymbolHelper.HasAttribute(symbol, "Emit");

    /// <summary>
    /// Reads [Emit("expression")] from a symbol.
    /// </summary>
    public static string? GetEmit(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name is "EmitAttribute" or "Emit");

        if (attr is { ConstructorArguments.Length: > 0 })
            return attr.ConstructorArguments[0].Value?.ToString();

        return null;
    }

    /// <summary>
    /// Converts PascalCase to camelCase for TS output.
    /// </summary>
    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (char.IsLower(name[0])) return name;
        var result = char.ToLowerInvariant(name[0]) + name[1..];
        // Escape JS/TS reserved words by appending underscore
        if (IsReservedWord(result))
            return result + "_";
        return result;
    }

    private static readonly HashSet<string> JsReservedWords =
    [
        "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for",
        "function", "if", "import", "in", "instanceof", "let", "new", "null", "return",
        "super", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var",
        "void", "while", "with", "yield", "async", "await", "of"
    ];

    private static bool IsReservedWord(string name) => JsReservedWords.Contains(name);
}
