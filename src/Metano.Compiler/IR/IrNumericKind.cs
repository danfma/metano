namespace Metano.Compiler.IR;

/// <summary>
/// The exact C# numeric kind of a conversion endpoint or of a built-in
/// operation, independent of how <see cref="IrPrimitive"/> folds the narrow
/// unsigned types (<c>uint</c> into <see cref="IrPrimitive.Int32"/>,
/// <c>ushort</c> into <see cref="IrPrimitive.Int16"/>, <c>sbyte</c> into
/// <see cref="IrPrimitive.Byte"/>). <c>nint</c> is <see cref="Int64"/> and
/// <c>nuint</c> is <see cref="UInt64"/>, since .NET runs them as 64-bit on
/// every supported host.
/// </summary>
public enum IrNumericKind
{
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Char,
    Single,
    Double,
    Decimal,
    BigInteger,
}
