using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// How a C# primitive exists at runtime in TypeScript, in one table (spec 011
/// FR-022, NFR-004): its <c>metano-runtime</c> descriptor constant, its
/// validating type check (which also checks the range of an integral kind)
/// and the <c>typeof</c> of its JS value. The folded integral kinds
/// (<c>sbyte</c>, <c>ushort</c>, <c>uint</c>) are told apart through
/// <see cref="IrPrimitiveTypeRef.ExactKind"/>.
/// </summary>
/// <param name="Descriptor">The descriptor constant (<c>Int32Type</c>).</param>
/// <param name="Validator">The validating check (<c>isInt32</c>); <c>null</c>
/// for a type that tests by its class (the temporal types, <c>object</c>).</param>
/// <param name="TypeofKind">The <c>typeof</c> of the JS value; <c>null</c> when
/// it is an object.</param>
/// <param name="ToStringTag">The <c>Object.prototype.toString</c> tag a value of
/// an object kind answers (<c>[object Temporal.PlainDate]</c>), the predicate its
/// descriptor's <c>test</c> uses too (FR-017); <c>null</c> for the others.</param>
internal sealed record TsRuntimePrimitive(
    string Descriptor,
    string? Validator,
    string? TypeofKind,
    string? ToStringTag = null
)
{
    private static readonly TsRuntimePrimitive SByte = new("SByteType", "isSByte", "number");

    private static readonly TsRuntimePrimitive UInt16 = new("UInt16Type", "isUInt16", "number");

    private static readonly TsRuntimePrimitive UInt32 = new("UInt32Type", "isUInt32", "number");

    /// <summary>The runtime shape of <paramref name="primitive"/>, refined by <paramref name="exactKind"/>.</summary>
    public static TsRuntimePrimitive? Of(IrPrimitive primitive, IrNumericKind? exactKind = null) =>
        exactKind switch
        {
            IrNumericKind.SByte => SByte,
            IrNumericKind.UInt16 => UInt16,
            IrNumericKind.UInt32 => UInt32,
            _ => OfPrimitive(primitive),
        };

    /// <summary>The runtime shape of an integral numeric kind (an enum's underlying type).</summary>
    public static TsRuntimePrimitive? OfIntegral(IrNumericKind kind) =>
        kind switch
        {
            IrNumericKind.SByte => SByte,
            IrNumericKind.Byte => OfPrimitive(IrPrimitive.Byte),
            IrNumericKind.Int16 => OfPrimitive(IrPrimitive.Int16),
            IrNumericKind.UInt16 => UInt16,
            IrNumericKind.Int32 => OfPrimitive(IrPrimitive.Int32),
            IrNumericKind.UInt32 => UInt32,
            IrNumericKind.Int64 => OfPrimitive(IrPrimitive.Int64),
            IrNumericKind.UInt64 => OfPrimitive(IrPrimitive.UInt64),
            _ => null,
        };

    private static TsRuntimePrimitive Temporal(string descriptor, string temporalClass) =>
        new(descriptor, null, null, $"[object Temporal.{temporalClass}]");

    private static TsRuntimePrimitive? OfPrimitive(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Boolean => new("BooleanType", "isBool", "boolean"),
            IrPrimitive.Byte => new("ByteType", "isByte", "number"),
            IrPrimitive.Int16 => new("Int16Type", "isInt16", "number"),
            IrPrimitive.Int32 => new("Int32Type", "isInt32", "number"),
            IrPrimitive.Int64 => new("Int64Type", "isInt64", "bigint"),
            IrPrimitive.UInt64 => new("UInt64Type", "isUInt64", "bigint"),
            IrPrimitive.Float32 => new("SingleType", "isFloat32", "number"),
            IrPrimitive.Float64 => new("DoubleType", "isFloat64", "number"),
            IrPrimitive.Decimal => new("DecimalType", "isDecimal", null),
            IrPrimitive.BigInteger => new("BigIntegerType", "isBigInt", "bigint"),
            IrPrimitive.String => new("StringType", "isString", "string"),
            IrPrimitive.Char => new("CharType", "isChar", "string"),
            IrPrimitive.Guid => new("GuidType", "isGuid", "string"),
            IrPrimitive.DateTime => Temporal("DateTimeType", "PlainDateTime"),
            IrPrimitive.DateTimeOffset => Temporal("DateTimeOffsetType", "ZonedDateTime"),
            IrPrimitive.DateOnly => Temporal("DateOnlyType", "PlainDate"),
            IrPrimitive.TimeOnly => Temporal("TimeOnlyType", "PlainTime"),
            IrPrimitive.TimeSpan => Temporal("TimeSpanType", "Duration"),
            IrPrimitive.Object => new("ObjectType", null, null),
            _ => null,
        };
}
