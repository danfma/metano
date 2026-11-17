using Metano.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The names the members a type's synthesized value equality declares are
/// spelled under (<see cref="IrValueEqualitySynthesis"/>): <c>Equals</c>,
/// <c>HashCode</c> (the spelling of <c>GetHashCode</c>) and, for a record,
/// <c>With</c> (the copy behind a <c>with</c> expression). The runtime calls
/// them by those names (<c>valueEquals</c>, <c>HashSet</c>, the <c>with</c>
/// lowering), so a user method spelled the same way that is not the one C#
/// lets replace the synthesized member (<c>Equals(R)</c>, <c>Equals(object)</c>)
/// yields the name: it becomes a dispatched group of its own, emitted under
/// its spec 013 fast-path name, and its call sites follow (#674).
/// </summary>
internal static class ValueEqualityMemberNames
{
    private static readonly string[] SynthesizedNames = ["Equals", "HashCode"];

    private const string CopyName = "With";

    /// <summary>
    /// Whether the methods <paramref name="type"/> declares under
    /// <paramref name="name"/> include an instance method that a synthesized
    /// value-equality member of the type would share its spelling with.
    /// </summary>
    public static bool AreClaimed(
        INamedTypeSymbol type,
        string name,
        IReadOnlyList<MethodGroupMember> members
    ) =>
        SynthesizesValueEquality(type)
        && IsSynthesizedSpelling(type, name)
        && members.Any(member =>
            !member.Symbol.IsStatic && !ReplacesSynthesizedEquals(member.Symbol)
        );

    /// <summary>
    /// <c>Equals(T other)</c> or <c>Equals(object other)</c> on the instance: the
    /// declaration C# uses instead of the synthesized <c>Equals</c>.
    /// </summary>
    public static bool ReplacesSynthesizedEquals(IMethodSymbol method) =>
        method is { Name: nameof(Equals), IsStatic: false, Parameters: [{ Type: var parameter }] }
        && (
            parameter.SpecialType is SpecialType.System_Object
            || SymbolEqualityComparer.Default.Equals(
                parameter.OriginalDefinition,
                method.ContainingType.OriginalDefinition
            )
        );

    private static bool SynthesizesValueEquality(INamedTypeSymbol type) =>
        (type.IsRecord || type.TypeKind is TypeKind.Struct)
        && !type.HasPlainObject()
        && !type.HasJsTuple()
        && !type.HasBranded()
        && !type.IsJsxComponent();

    private static bool IsSynthesizedSpelling(INamedTypeSymbol type, string name) =>
        SynthesizedNames.Any(synthesized => SpelledAs(name, synthesized))
        || type.IsRecord && SpelledAs(name, CopyName);

    /// <summary>The same name but for the case of its first letter (<c>hashCode</c>, <c>HashCode</c>).</summary>
    private static bool SpelledAs(string name, string synthesized) =>
        name.Length == synthesized.Length
        && char.ToUpperInvariant(name[0]) == synthesized[0]
        && string.CompareOrdinal(name, 1, synthesized, 1, synthesized.Length - 1) == 0;
}
