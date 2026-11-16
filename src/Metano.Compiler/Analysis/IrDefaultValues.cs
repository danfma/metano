using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// How the default value C# gives a type (<c>default(T)</c>, an unassigned
/// field) is spelled, decided once from the IR type: the core's default-value
/// policy builds the expression positions from it, and a target prints an
/// implicit member initializer from it.
/// </summary>
public enum IrDefaultValueKind
{
    /// <summary>No value the target can spell: a user struct that declares a constructor, a tuple, a <c>[StringEnum]</c> without a zero member, a reference type left unassigned.</summary>
    None,

    /// <summary><c>null</c>: a nullable type.</summary>
    Null,

    /// <summary>The zero of a primitive: <c>false</c>, <c>0</c>, <c>0n</c>, <c>'\0'</c>, <c>0m</c>, <c>Guid.Empty</c>.</summary>
    Zero,

    /// <summary>The enum member whose value is <c>0</c>.</summary>
    EnumZeroMember,

    /// <summary>A <c>0</c> asserted to a numeric enum that declares no zero member.</summary>
    EnumZeroAssertion,

    /// <summary>
    /// A parameterless construction of a transpiled struct that declares no
    /// constructor (<see cref="IrNamedTypeSemantics.IsDefaultConstructible"/>):
    /// every field starts at its own default, the zeroed value C# stores.
    /// </summary>
    StructConstruction,
}

/// <summary>The default-value policy over IR types (<see cref="IrDefaultValueKind"/>).</summary>
public static class IrDefaultValues
{
    /// <summary>How the default of <paramref name="type"/> is spelled.</summary>
    public static IrDefaultValueKind KindOf(IrTypeRef type) =>
        type switch
        {
            IrNullableTypeRef => IrDefaultValueKind.Null,
            IrPrimitiveTypeRef { Primitive: var primitive } when HasZeroLiteral(primitive) =>
                IrDefaultValueKind.Zero,
            IrNamedTypeRef { Semantics: { } semantics } => KindOf(semantics),
            _ => IrDefaultValueKind.None,
        };

    private static IrDefaultValueKind KindOf(IrNamedTypeSemantics semantics) =>
        semantics switch
        {
            {
                Kind: IrNamedTypeKind.NumericEnum or IrNamedTypeKind.StringEnum,
                EnumZeroMember: not null
            } => IrDefaultValueKind.EnumZeroMember,
            { Kind: IrNamedTypeKind.NumericEnum } => IrDefaultValueKind.EnumZeroAssertion,
            { IsDefaultConstructible: true } => IrDefaultValueKind.StructConstruction,
            _ => IrDefaultValueKind.None,
        };

    private static bool HasZeroLiteral(IrPrimitive primitive) =>
        primitive
            is IrPrimitive.Boolean
                or IrPrimitive.Byte
                or IrPrimitive.Int16
                or IrPrimitive.Int32
                or IrPrimitive.Int64
                or IrPrimitive.UInt64
                or IrPrimitive.Float32
                or IrPrimitive.Float64
                or IrPrimitive.Decimal
                or IrPrimitive.BigInteger
                or IrPrimitive.Char
                or IrPrimitive.Guid;
}
