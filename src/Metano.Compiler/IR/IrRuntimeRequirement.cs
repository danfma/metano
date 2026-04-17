namespace Metano.Compiler.IR;

/// <summary>
/// A runtime helper dependency required by the IR. Each backend maps these to
/// concrete imports from its own runtime library (e.g., TypeScript maps
/// <c>("HashCode", Hashing)</c> to <c>import { HashCode } from "metano-runtime"</c>).
/// </summary>
/// <param name="HelperName">Semantic name of the helper (e.g., <c>"HashCode"</c>,
/// <c>"Enumerable"</c>, <c>"UUID"</c>).</param>
/// <param name="Category">Functional category of the helper.</param>
public sealed record IrRuntimeRequirement(string HelperName, IrRuntimeCategory Category);

/// <summary>
/// Categories of runtime helpers.
/// </summary>
public enum IrRuntimeCategory
{
    /// <summary>Equality comparison helpers (e.g., deep equals).</summary>
    Equality,

    /// <summary>Hash code computation helpers.</summary>
    Hashing,

    /// <summary>Collection utilities (Enumerable, LINQ).</summary>
    Collection,

    /// <summary>Date/time (Temporal) polyfills or helpers.</summary>
    Temporal,

    /// <summary>Type guard / runtime type check functions.</summary>
    TypeGuard,

    /// <summary>Serialization helpers (JSON, etc.).</summary>
    Serialization,

    /// <summary>Branded/opaque type utilities (UUID, etc.).</summary>
    BrandedType,

    /// <summary>Primitive runtime type discriminators emitted by overload
    /// dispatchers (<c>isInt32</c>, <c>isString</c>, <c>isBool</c>, …).</summary>
    PrimitiveTypeCheck,

    /// <summary>Event subscription helpers (e.g., the <c>delegateAdd</c> /
    /// <c>delegateRemove</c> pair used for C# <c>event</c> accessors).</summary>
    EventHandling,
}
