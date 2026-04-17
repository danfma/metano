using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reads Metano attributes from a Roslyn <see cref="ISymbol"/> and produces the
/// corresponding <see cref="IrAttribute"/> list. Backends consult these to apply
/// semantic renames, ignore flags, etc.
/// </summary>
public static class IrAttributeExtractor
{
    /// <summary>
    /// Builds an attribute list for a symbol. Returns <c>null</c> when no
    /// <c>[Name]</c> attributes apply.
    /// <para>
    /// Multiple <c>[Name]</c> occurrences on the same symbol collapse into a
    /// single <see cref="IrAttribute"/> whose <c>Arguments</c> dictionary has
    /// one entry per form:
    /// <list type="bullet">
    ///   <item><c>"Value"</c> → the untargeted <c>[Name("…")]</c>.</item>
    ///   <item><c>"TypeScript"</c>, <c>"Dart"</c>, … → per-target
    ///   <c>[Name(target, "…")]</c> occurrences, keyed by
    ///   <c>TargetLanguage</c> names.</item>
    /// </list>
    /// Target-specific naming policies look up their matching key first and
    /// fall back to <c>"Value"</c> when absent.
    /// </para>
    /// </summary>
    public static IReadOnlyList<IrAttribute>? Extract(ISymbol symbol)
    {
        Dictionary<string, object?>? args = null;
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("NameAttribute" or "Name"))
                continue;
            if (attr.ConstructorArguments.Length == 0)
                continue;

            // `[Name("x")]` — a single string arg → untargeted.
            if (
                attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string untargetedName
            )
            {
                (args ??= new())["Value"] = untargetedName;
                continue;
            }

            // `[Name(TargetLanguage.Dart, "x")]` — enum value (int) + string.
            if (
                attr.ConstructorArguments.Length >= 2
                && attr.ConstructorArguments[0].Value is int targetValue
                && attr.ConstructorArguments[1].Value is string perTargetName
                && System.Enum.IsDefined(typeof(TargetLanguage), targetValue)
            )
            {
                var key = ((TargetLanguage)targetValue).ToString();
                (args ??= new())[key] = perTargetName;
            }
        }

        return args is null ? null : [new IrAttribute("Name", args)];
    }
}
