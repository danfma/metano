using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one visibility table every TypeScript emission site reads (issue #445).
/// Before the fix, members, overload dispatchers and constructors each carried
/// a private copy of the mapping, and the copies disagreed on the assembly-wide
/// band: the dispatcher emitted an <c>internal</c> method as a private
/// dispatcher (TS2341 from any other class in the package), while the class
/// bridge emitted a <c>protected internal</c> property as <c>protected</c>
/// (TS2445 from a non-derived class). Now every site reads this table:
/// <list type="bullet">
///   <item><c>private</c> → <c>private</c>;</item>
///   <item><c>private protected</c> → <c>public</c> for class members (the
///     bridge skips such members upstream, so the arm is unreachable there)
///     and <c>private</c> for constructors (<see cref="TsConstructorModifier.Of"/>
///     narrows it before the table applies);</item>
///   <item><c>protected</c> → <c>protected</c>;</item>
///   <item><c>internal</c> / <c>protected internal</c> → <c>public</c> — the
///     assembly-wide band is call-reachable from any class of the emitting
///     package — and the member carries <c>/** @internal *&#47;</c> (see
///     <see cref="InternalJSDocOf"/>).</item>
/// </list>
/// </summary>
public static class IrToTsVisibilityMapper
{
    /// <summary>
    /// Maps IR visibility to the TS accessibility keyword the printer renders.
    /// </summary>
    public static TsAccessibility Of(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Private => TsAccessibility.Private,
            IrVisibility.Protected => TsAccessibility.Protected,
            _ => TsAccessibility.Public,
        };

    /// <summary>
    /// Whether the visibility lands in the assembly-wide band whose TS members
    /// are public but tagged <c>@internal</c>: C# <c>internal</c> and
    /// <c>protected internal</c>.
    /// </summary>
    public static bool IsInternalBand(IrVisibility visibility) =>
        visibility is IrVisibility.Internal or IrVisibility.ProtectedInternal;

    /// <summary>
    /// The <c>/** @internal *&#47;</c> JSDoc for a member of the assembly-wide
    /// band, <c>null</c> for every other visibility.
    /// </summary>
    public static TsJSDoc? InternalJSDocOf(IrVisibility visibility) =>
        IsInternalBand(visibility) ? new TsJSDoc(["@internal"]) : null;
}
