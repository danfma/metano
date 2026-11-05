using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one table that maps an IR visibility to the TypeScript declaration
/// emitted for it — every member, overload signature, constructor, promoted
/// member and module variable reads it:
/// <list type="bullet">
///   <item><c>public</c> → <c>public</c>;</item>
///   <item><c>internal</c> / <c>protected internal</c> → <c>public</c> tagged
///     <c>/** @internal *&#47;</c>: any class of the emitting package may reach
///     the member, and <c>stripInternal</c> drops it from the <c>.d.ts</c>;</item>
///   <item><c>protected</c> → <c>protected</c>;</item>
///   <item><c>private</c> / <c>private protected</c> → <c>private</c>.</item>
/// </list>
/// </summary>
public static class IrToTsVisibilityMapper
{
    private static readonly TsJSDoc InternalTag = new(["@internal"]);

    /// <summary>The TypeScript accessibility and JSDoc of <paramref name="visibility"/>.</summary>
    public static TsMemberVisibility Map(IrVisibility visibility) =>
        new(AccessibilityOf(visibility), IsAssemblyScoped(visibility) ? InternalTag : null);

    /// <summary>
    /// The most permissive of the visibilities that share one TypeScript
    /// declaration (an overload group, a constructor group): public, then
    /// <c>internal</c> / <c>protected internal</c>, then <c>protected</c>,
    /// then <c>private</c> / <c>private protected</c>.
    /// </summary>
    public static IrVisibility MostPermissiveOf(IEnumerable<IrVisibility> visibilities) =>
        visibilities.MinBy(RestrictionRankOf);

    private static TsAccessibility AccessibilityOf(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Public or IrVisibility.Internal or IrVisibility.ProtectedInternal =>
                TsAccessibility.Public,
            IrVisibility.Protected => TsAccessibility.Protected,
            IrVisibility.Private or IrVisibility.PrivateProtected => TsAccessibility.Private,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null),
        };

    private static int RestrictionRankOf(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Public => 0,
            IrVisibility.Internal or IrVisibility.ProtectedInternal => 1,
            IrVisibility.Protected => 2,
            IrVisibility.Private or IrVisibility.PrivateProtected => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null),
        };

    private static bool IsAssemblyScoped(IrVisibility visibility) =>
        visibility is IrVisibility.Internal or IrVisibility.ProtectedInternal;
}
