namespace Metano.Compiler.IR;

/// <summary>
/// Semantic primitive types — C# concepts, not target renderings.
/// Each backend maps these to its own type system
/// (e.g., <see cref="Guid"/> → <c>UUID</c> in TypeScript, <c>String</c> in Dart).
/// </summary>
public enum IrPrimitive
{
    Boolean,
    Byte,
    Int16,
    Int32,
    Int64,

    /// <summary>
    /// C#'s <c>ulong</c>: an unsigned 64-bit integer, kept apart from
    /// <see cref="Int64"/> because a backend that renders 64-bit integers
    /// exactly needs the signedness. The narrower unsigned types keep folding
    /// into their signed counterparts, whose values fit the same rendering.
    /// </summary>
    UInt64,
    Float32,
    Float64,
    Decimal,
    BigInteger,
    String,
    Char,
    Void,
    Object,
    Guid,
    DateTime,
    DateTimeOffset,
    DateOnly,
    TimeOnly,
    TimeSpan,
}
