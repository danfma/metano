using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// TypeScript/JavaScript-specific naming and attribute helpers:
/// camelCase identifier conversion, JS reserved word escaping, and the
/// [Emit("$0.foo($1)")] string template attribute.
///
/// Target-agnostic helpers (kebab-case, attribute presence, [Transpile]/[Ignore]/etc.)
/// live in <see cref="Metano.Compiler.SymbolHelper"/>.
/// </summary>
public static class TypeScriptNaming
{
    /// <summary>
    /// The words an ES module (strict mode) rejects as a binding or label name on
    /// top of <see cref="JsReservedWords"/> — and <c>eval</c> / <c>arguments</c>,
    /// which strict mode forbids binding. Property and member names may still
    /// carry them (<c>x.public</c>), so only identifier positions consult this
    /// set (<see cref="EscapeIfReserved"/>, <see cref="ToLabel"/>); the member
    /// naming (<see cref="ToCamelCase"/>) does not.
    /// </summary>
    private static readonly HashSet<string> StrictModeReservedWords =
    [
        "arguments",
        "eval",
        "implements",
        "interface",
        "package",
        "private",
        "protected",
        "public",
        "static",
    ];

    /// <summary>
    /// Reads [Emit("expression")] from a symbol.
    /// </summary>
    public static string? GetEmit(ISymbol symbol)
    {
        var attr = symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "EmitAttribute" or "Emit");

        if (attr is { ConstructorArguments.Length: > 0 })
            return attr.ConstructorArguments[0].Value?.ToString();

        return null;
    }

    /// <summary>
    /// Converts PascalCase to camelCase for TS output as a variable / parameter
    /// identifier. Reserved words get an underscore suffix because they can't be
    /// used as bare identifiers in JS (<c>let delete = …</c> is illegal).
    /// </summary>
    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (char.IsLower(name[0]))
            return name;
        var result = char.ToLowerInvariant(name[0]) + name[1..];
        // Escape JS/TS reserved words by appending underscore
        if (IsReservedWord(result))
            return result + "_";
        return result;
    }

    /// <summary>
    /// camelCase variant for class members and object property names — i.e., names
    /// that always appear in property position (after a dot, inside a class body, or
    /// as a key in an object literal). Reserved words are NOT escaped because JS
    /// allows them as property names (<c>obj.delete</c>, <c>class Foo { delete() {} }</c>).
    /// Use this at declaration sites (method/property/field declarations) AND at
    /// reference sites (member access, static member, instance member) so the two
    /// halves stay in agreement.
    /// </summary>
    public static string ToCamelCaseMember(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (char.IsLower(name[0]))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static readonly HashSet<string> JsReservedWords =
    [
        "break",
        "case",
        "catch",
        "class",
        "const",
        "continue",
        "debugger",
        "default",
        "delete",
        "do",
        "else",
        "enum",
        "export",
        "extends",
        "false",
        "finally",
        "for",
        "function",
        "if",
        "import",
        "in",
        "instanceof",
        "let",
        "new",
        "null",
        "return",
        "super",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "undefined",
        "var",
        "void",
        "while",
        "with",
        "yield",
        "async",
        "await",
        "of",
    ];

    private static bool IsReservedWord(string name) => JsReservedWords.Contains(name);

    private static bool IsReservedIdentifier(string name) =>
        IsReservedWord(name) || StrictModeReservedWords.Contains(name);

    /// <summary>
    /// Appends a trailing underscore to <paramref name="name"/> when
    /// it is a JavaScript / TypeScript reserved word (strict-mode words
    /// included, <see cref="StrictModeReservedWords"/>), leaving any
    /// other identifier unchanged. Used at top-level declaration
    /// sites (<c>export const/let/function</c>) where the language
    /// does not accept reserved identifiers — distinct from class
    /// members and property keys, which can carry reserved names
    /// freely.
    /// </summary>
    public static string EscapeIfReserved(string name) =>
        IsReservedIdentifier(name) ? name + "_" : name;

    /// <summary>
    /// The JavaScript spelling of a C# statement label (#772): the C# name,
    /// so two labels C# tells apart stay apart, with a reserved word prefixed
    /// by <c>$</c>, which no C# identifier holds, so the escape never meets
    /// another label.
    /// </summary>
    public static string ToLabel(string name) => IsReservedIdentifier(name) ? "$" + name : name;
}
