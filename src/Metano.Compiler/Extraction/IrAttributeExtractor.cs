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
        Dictionary<string, object?>? nameArgs = null;
        var others = new List<IrAttribute>();
        foreach (var attr in symbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.Name;
            if (attrName is "NameAttribute" or "Name")
            {
                if (attr.ConstructorArguments.Length == 0)
                    continue;

                // `[Name("x")]` — a single string arg → untargeted.
                if (
                    attr.ConstructorArguments.Length == 1
                    && attr.ConstructorArguments[0].Value is string untargetedName
                )
                {
                    (nameArgs ??= new())[IrAttribute.NameOverrideValueArgument] = untargetedName;
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
                    (nameArgs ??= new())[key] = perTargetName;
                }
                continue;
            }

            // Surface a small set of behavior-shaping attributes so backends
            // can branch without re-reading the Roslyn symbol. Add to this
            // list as new attributes need bridge-side dispatch.
            if (
                attr.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Metano.Annotations"
                && attrName is "ObjectArgsAttribute" or "ObjectArgs"
            )
            {
                others.Add(new IrAttribute("ObjectArgs"));
            }
        }

        var result = new List<IrAttribute>();
        if (nameArgs is not null)
            result.Add(new IrAttribute(IrAttribute.NameOverrideAttribute, nameArgs));
        result.AddRange(others);
        return result.Count == 0 ? null : result;
    }
}
