using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one table that maps an IR visibility to the TypeScript declaration
/// emitted for it — every member, overload signature, constructor, promoted
/// member and module variable reads it:
/// <list type="bullet">
///   <item><c>public</c> / <c>protected internal</c> → <c>public</c>: a
///     derived class in another package may use a <c>protected internal</c>
///     member, so it stays in the <c>.d.ts</c>;</item>
///   <item><c>internal</c> → <c>public</c> tagged <c>/** @internal *&#47;</c>:
///     any class of the emitting package may reach the member, and
///     <c>stripInternal</c> drops it from the <c>.d.ts</c>;</item>
///   <item><c>protected</c> → <c>protected</c>;</item>
///   <item><c>private protected</c> → <c>protected</c> tagged
///     <c>/** @internal *&#47;</c>: the class and its derived classes of the
///     package reach the member, and <c>stripInternal</c> drops it;</item>
///   <item><c>private</c> → <c>private</c>.</item>
/// </list>
/// A constructor reads <see cref="MapConstructor"/>, which keeps FR-049's
/// <c>private</c> for <c>private protected</c>.
/// </summary>
public static class IrToTsVisibilityMapper
{
    private static readonly TsJSDoc InternalTag = new(["@internal"]);

    /// <summary>The TypeScript accessibility and JSDoc of <paramref name="visibility"/>.</summary>
    public static TsMemberVisibility Map(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Public or IrVisibility.ProtectedInternal => new(
                TsAccessibility.Public,
                null
            ),
            IrVisibility.Internal => new(TsAccessibility.Public, InternalTag),
            IrVisibility.Protected => new(TsAccessibility.Protected, null),
            IrVisibility.PrivateProtected => new(TsAccessibility.Protected, InternalTag),
            IrVisibility.Private => new(TsAccessibility.Private, null),
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null),
        };

    /// <summary>
    /// The TypeScript accessibility and JSDoc of a constructor declared
    /// <paramref name="visibility"/>: <see cref="Map"/>, except that a
    /// <c>private protected</c> constructor emits an untagged
    /// <c>private constructor</c> (FR-049), which
    /// <see cref="ConstructorAccessPolicy"/> lowers to <c>protected</c> when
    /// an emitted class extends the type.
    /// </summary>
    public static TsMemberVisibility MapConstructor(IrVisibility visibility) =>
        visibility is IrVisibility.PrivateProtected
            ? new(TsAccessibility.Private, null)
            : Map(visibility);

    /// <summary>
    /// The most permissive of the accessibilities that share one TypeScript
    /// declaration (an overload group, a constructor group): public, then
    /// <c>protected</c>, then <c>private</c>.
    /// </summary>
    public static TsAccessibility MostPermissiveOf(IEnumerable<TsAccessibility> accessibilities) =>
        accessibilities.MinBy(RestrictionOf);

    public static bool IsNarrowerThan(this TsAccessibility modifier, TsAccessibility other) =>
        RestrictionOf(modifier) > RestrictionOf(other);

    /// <summary>
    /// The JSDoc of one overload signature of a group emitted
    /// <paramref name="group"/>: its own tag, or <c>@internal</c> when the
    /// signature is narrower than the group. TypeScript makes every signature
    /// share the group's accessibility (TS2385), so <c>stripInternal</c>
    /// hides the narrower one instead.
    /// </summary>
    public static TsJSDoc? SignatureJSDocOf(TsMemberVisibility signature, TsAccessibility group) =>
        signature.JSDoc ?? (signature.Accessibility.IsNarrowerThan(group) ? InternalTag : null);

    /// <summary>How restrictive a modifier is: public 0, protected 1, private 2.</summary>
    private static int RestrictionOf(TsAccessibility modifier) =>
        modifier switch
        {
            TsAccessibility.Public or TsAccessibility.None => 0,
            TsAccessibility.Protected => 1,
            TsAccessibility.Private => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(modifier), modifier, null),
        };
}
