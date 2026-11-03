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
internal sealed record IrToTsBridgeContext(
    DeclarativeMappingRegistry? Bcl,
    PerClassBackingFieldScope ClassFields
)
{
    /// <summary>
    /// The context of a position outside class emission (module functions, plain
    /// objects, branded structs, static operators, JSX components, entry-point
    /// bodies): no backing field is ever recognized. Inside a class emission the
    /// emitter passes the class's own scope everywhere, including parameter
    /// defaults and member initializers.
    /// </summary>
    public static IrToTsBridgeContext For(DeclarativeMappingRegistry? bclRegistry) =>
        new(bclRegistry, PerClassBackingFieldScope.None);
}
