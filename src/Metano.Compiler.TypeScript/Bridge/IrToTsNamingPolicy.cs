using Metano.Annotations;
using Metano.Compiler.IR;
using Metano.Transformation;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// TypeScript-specific naming decisions applied when lowering IR to TS AST.
/// Keeps the IR target-agnostic (PascalCase, no reserved-word escaping) while
/// centralizing JS/TS casing rules in one place.
/// </summary>
public static class IrToTsNamingPolicy
{
    /// <summary>
    /// Resolves the emitted name for an interface property or method: honors
    /// <c>[Name("x")]</c> overrides when present, otherwise applies camelCase with
    /// reserved-word escaping.
    /// </summary>
    public static string ToInterfaceMemberName(
        string irName,
        IReadOnlyList<IrAttribute>? attributes
    )
    {
        var overrideName = FindNameOverride(attributes);
        return overrideName ?? TypeScriptNaming.ToCamelCase(irName);
    }

    /// <summary>
    /// Resolves the parameter name: camelCase with reserved-word escaping
    /// (<c>delete</c> → <c>delete_</c>) since parameters are used as bare identifiers.
    /// </summary>
    public static string ToParameterName(string irName) => TypeScriptNaming.ToCamelCase(irName);

    /// <summary>
    /// The type name is not renamed — matches <c>TypeTransformer.GetTsTypeName</c>
    /// which honors <c>[Name]</c> on the symbol. The extractor already places name
    /// overrides into <see cref="IrAttribute"/> entries; this helper looks them up.
    /// </summary>
    public static string ToTypeName(string irName, IReadOnlyList<IrAttribute>? attributes)
    {
        var overrideName = FindNameOverride(attributes);
        return overrideName ?? irName;
    }

    /// <summary>
    /// For enum members in string enums, the emitted string value honors <c>[Name]</c>
    /// when present and otherwise uses the original member name in its C# casing.
    /// </summary>
    public static string ToEnumMemberName(string irName, IReadOnlyList<IrAttribute>? attributes)
    {
        var overrideName = FindNameOverride(attributes);
        return overrideName ?? irName;
    }

    /// <summary>
    /// Top-level function name: honors <c>[Name]</c> verbatim when present
    /// (so users can opt into a reserved word or a wire shape unchanged) and
    /// otherwise camelCases the IR name.
    /// </summary>
    public static string ToFunctionName(string irName, IReadOnlyList<IrAttribute>? attributes)
    {
        var overrideName = FindNameOverride(attributes);
        return overrideName ?? TypeScriptNaming.ToCamelCase(irName);
    }

    /// <summary>
    /// Class method name: honors <c>[Name]</c> verbatim when present,
    /// otherwise camelCases the IR name <em>without</em> reserved-word
    /// escaping (a method called <c>Delete</c> becomes <c>delete()</c>, not
    /// <c>delete_()</c>) — JS lets methods use any identifier as a member
    /// access. Both this declaration helper and the call-site bridge route
    /// through the same lowering so the two halves stay in agreement.
    /// </summary>
    public static string ToMethodName(string irName, IReadOnlyList<IrAttribute>? attributes)
    {
        var overrideName = FindNameOverride(attributes);
        return overrideName ?? TypeScriptNaming.ToCamelCaseMember(irName);
    }

    /// <summary>
    /// Reads the <c>[Name]</c> override, preferring the TypeScript-specific
    /// <c>TypeScript = "…"</c> named argument when present and falling back to
    /// the positional <c>Value</c>. Mirrors the Dart naming policy's shape so
    /// the same attribute can carry per-target renames at once.
    /// </summary>
    public static string? FindNameOverride(IReadOnlyList<IrAttribute>? attributes)
    {
        if (attributes is null)
            return null;

        var targetKey = nameof(TargetLanguage.TypeScript);
        foreach (var attr in attributes)
        {
            if (attr.Name != "Name" || attr.Arguments is null)
                continue;
            if (
                attr.Arguments.TryGetValue(targetKey, out var perTarget)
                && perTarget is string perTargetS
            )
                return perTargetS;
            if (attr.Arguments.TryGetValue("Value", out var value) && value is string s)
                return s;
        }

        return null;
    }
}
