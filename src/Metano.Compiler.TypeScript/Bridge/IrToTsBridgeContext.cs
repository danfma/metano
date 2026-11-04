using Metano.Compiler.Mappings;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one context the TypeScript lowering bridges thread through a class emission:
/// the declarative BCL mappings (<see cref="Bcl"/>) and the per-class backing fields
/// of the class being emitted (<see cref="ClassFields"/> —
/// <see cref="PerClassBackingFieldScope.None"/> outside any class emission).
/// <para>
/// Replacing the registry + scope parameter pair with one object keeps every internal
/// bridge method explicit about the whole context, and the non-nullable scope makes
/// forgetting it a compile error rather than a silent no-scope lowering.
/// </para>
/// </summary>
/// <param name="Bcl">The declarative BCL mappings, or <c>null</c> to emit raw
/// member accesses and calls.</param>
/// <param name="ClassFields">The backing fields in scope; never <c>null</c>.</param>
/// <param name="TypeOverrides">The <c>[ExportFromBcl]</c> type overrides
/// (<c>decimal</c> → <c>Decimal</c>) the overload dispatchers apply to the
/// parameter types and runtime checks they emit, matching the class
/// emitter's own signatures. Required, so a class emission cannot forget it;
/// <c>null</c> (outside class emission, see <see cref="For"/>) keeps the
/// default mapping.</param>
internal sealed record IrToTsBridgeContext(
    DeclarativeMappingRegistry? Bcl,
    PerClassBackingFieldScope ClassFields,
    IrToTsTypeOverrides? TypeOverrides
)
{
    /// <summary>
    /// The context of a position outside class emission (module functions, plain
    /// objects, branded structs, static operators, JSX components, entry-point
    /// bodies): no backing field is ever recognized. Inside a class emission the
    /// emitter passes the class's own scope everywhere, including parameter
    /// defaults and member initializers. No type override applies there: those
    /// positions map their types without the class emitter's overrides.
    /// </summary>
    public static IrToTsBridgeContext For(DeclarativeMappingRegistry? bclRegistry) =>
        new(bclRegistry, PerClassBackingFieldScope.None, TypeOverrides: null);
}
