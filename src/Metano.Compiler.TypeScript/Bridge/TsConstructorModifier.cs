using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript modifier of a constructor declared with a given IR
/// visibility (FR-049): <c>private</c> and <c>private protected</c> map to
/// <c>private</c>, <c>protected</c> to <c>protected</c>, every other
/// accessibility to public. TypeScript orders them public, protected,
/// private from the least to the most restrictive.
/// </summary>
public static class TsConstructorModifier
{
    public static TsAccessibility Of(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Private or IrVisibility.PrivateProtected => TsAccessibility.Private,
            IrVisibility.Protected => TsAccessibility.Protected,
            _ => TsAccessibility.Public,
        };

    /// <summary>How restrictive a modifier is: public 0, protected 1, private 2.</summary>
    public static int RestrictionOf(TsAccessibility modifier) =>
        modifier switch
        {
            TsAccessibility.Private => 2,
            TsAccessibility.Protected => 1,
            _ => 0,
        };

    public static bool IsNarrowerThan(this TsAccessibility modifier, TsAccessibility other) =>
        RestrictionOf(modifier) > RestrictionOf(other);
}
