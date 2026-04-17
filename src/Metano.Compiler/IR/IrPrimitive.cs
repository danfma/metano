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
