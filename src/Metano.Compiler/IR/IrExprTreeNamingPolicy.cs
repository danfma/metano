using Metano.Annotations;

namespace Metano.Compiler.IR;

/// <summary>
/// Target-aware casing rules applied when lowering captured expression trees
/// (<see cref="IrExprTreeNode"/>) to the per-target object literals that the
/// runtime <c>QueryableMeta.tree</c> consumes.
/// <para>
/// The walker keeps the original C# PascalCase on <see cref="IrExprMember.Member"/>
/// and <see cref="IrExprCall.Method"/>; each backend resolves the emitted string
/// through this helper so the tree's <c>member</c> / <c>method</c> keys match the
/// lowered surface (the actual property the call site dereferences).
/// </para>
/// <para>
/// Both TypeScript and Dart use lowerCamelCase for instance members, so the
/// implementation is shared today. Future targets (Kotlin, Swift) can extend the
/// switch with their own conventions without touching the bridges.
/// </para>
/// </summary>
public static class IrExprTreeNamingPolicy
{
    /// <summary>
    /// Resolves the emitted name for an <see cref="IrExprMember"/> or
    /// <see cref="IrExprCall"/> string in the captured tree. Member-position
    /// names do not need reserved-word escaping in either TS or Dart, so the
    /// lowering is the plain camelCase variant in both cases.
    /// <para>
    /// <paramref name="target"/> is honored explicitly when set; <c>null</c>
    /// falls back to the TypeScript convention (the only target wired today
    /// for expression-tree emission). Dart will reuse this policy when the
    /// Dart bridge gains <c>ExprTree</c> emission.
    /// </para>
    /// </summary>
    public static string MemberCasing(string name, TargetLanguage? target = null) =>
        target switch
        {
            TargetLanguage.TypeScript => ToCamelCase(name),
            TargetLanguage.Dart => ToCamelCase(name),
            _ => ToCamelCase(name),
        };

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (char.IsLower(name[0]))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
