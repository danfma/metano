using Metano.Annotations;
using Metano.Compiler.IR;

namespace Metano.Dart.Bridge;

/// <summary>
/// Centralizes Dart-specific naming rules. Dart follows:
/// <list type="bullet">
///   <item>Types: <c>PascalCase</c> (same as C#).</item>
///   <item>Members, parameters, locals: <c>lowerCamelCase</c>.</item>
///   <item>Files: <c>snake_case</c> with <c>.dart</c> extension.</item>
///   <item>Private members: leading underscore (<c>_name</c>).</item>
/// </list>
/// </summary>
public static class IrToDartNamingPolicy
{
    public static string ToTypeName(string irName, IReadOnlyList<IrAttribute>? attributes) =>
        FindNameOverride(attributes) ?? irName;

    public static string ToMemberName(string irName, IReadOnlyList<IrAttribute>? attributes)
    {
        var overrideName = FindNameOverride(attributes);
        if (overrideName is not null)
            return overrideName;
        return ToCamelCase(irName);
    }

    public static string ToParameterName(string irName) => ToCamelCase(irName);

    /// <summary>
    /// The file basename (without extension) for a type. Dart convention is
    /// <c>snake_case</c>.
    /// </summary>
    public static string ToFileName(string typeName) => ToSnakeCase(typeName) + ".dart";

    private static string ToCamelCase(string name)
    {
        if (name.Length == 0)
            return name;
        if (char.IsLower(name[0]))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string ToSnakeCase(string pascal)
    {
        if (pascal.Length == 0)
            return pascal;
        var sb = new System.Text.StringBuilder(pascal.Length + 4);
        sb.Append(char.ToLowerInvariant(pascal[0]));
        for (var i = 1; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c))
            {
                sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads the <c>[Name]</c> override, preferring the Dart-specific
    /// <c>Dart = "…"</c> named argument when present and falling back to the
    /// positional <c>Value</c>. Letting a single attribute carry multiple
    /// target overrides avoids having consumers annotate the same symbol
    /// repeatedly for each target.
    /// </summary>
    private static string? FindNameOverride(IReadOnlyList<IrAttribute>? attributes)
    {
        if (attributes is null)
            return null;
        var targetKey = nameof(TargetLanguage.Dart);
        foreach (var attr in attributes)
        {
            if (attr.Name != "Name" || attr.Arguments is null)
                continue;
            if (
                attr.Arguments.TryGetValue(targetKey, out var perTargetV)
                && perTargetV is string perTargetS
            )
                return perTargetS;
            if (attr.Arguments.TryGetValue("Value", out var v) && v is string s)
                return s;
        }
        return null;
    }
}
