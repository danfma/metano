namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The names of the <c>metano-runtime/system/int64</c> helpers the TypeScript
/// bridge emits (spec 012 contracts/runtime-int64.md), the single place they
/// are spelled, with the selectors that pick a variant by signedness and
/// context.
/// </summary>
internal static class Int64RuntimeHelpers
{
    public const string CheckedInt64 = "checkedInt64";
    public const string CheckedUInt64 = "checkedUInt64";
    public const string CheckedNarrow = "checkedNarrow";
    public const string CheckedInt32 = "checkedInt32";
    public const string CheckedUInt32 = "checkedUInt32";
    public const string CheckedNarrowInt32 = "checkedNarrowInt32";
    public const string MapNullable = "mapNullable";
    public const string Int64FromDouble = "int64FromDouble";
    public const string Int64FromDoubleChecked = "int64FromDoubleChecked";
    public const string UInt64FromDouble = "uint64FromDouble";
    public const string UInt64FromDoubleChecked = "uint64FromDoubleChecked";
    public const string Int32FromDoubleUnchecked = "int32FromDouble";
    public const string Int32FromDoubleChecked = "int32FromDoubleChecked";
    public const string UInt32FromDoubleUnchecked = "uint32FromDouble";
    public const string UInt32FromDoubleChecked = "uint32FromDoubleChecked";
    public const string Int64ToSingle = "int64ToSingle";
    public const string Int64FromDecimal = "int64FromDecimal";
    public const string UInt64FromDecimal = "uint64FromDecimal";
    public const string Int64FromBigInteger = "int64FromBigInteger";
    public const string UInt64FromBigInteger = "uint64FromBigInteger";
    public const string DivInt64 = "divInt64";
    public const string RemInt64 = "remInt64";
    public const string DivUInt64 = "divUInt64";
    public const string RemUInt64 = "remUInt64";
    public const string FormatInt64 = "formatInt64";
    public const string Int32FromBigInteger = "int32FromBigInteger";
    public const string UInt32FromBigInteger = "uint32FromBigInteger";
    public const string NarrowFromBigInteger = "narrowFromBigInteger";
    public const string DoubleFromBigInteger = "doubleFromBigInteger";
    public const string DecimalTextFromBigInteger = "decimalTextFromBigInteger";
    public const string BigIntegerFromDouble = "bigIntegerFromDouble";
    public const string BigIntegerFromDecimal = "bigIntegerFromDecimal";
    public const string SumInt64 = "sumInt64";
    public const string AverageInt64 = "averageInt64";
    public const string AverageNullableInt64 = "averageNullableInt64";
    public const string MinInt64Of = "minInt64Of";
    public const string MinUInt64Of = "minUInt64Of";
    public const string MinNullableInt64Of = "minNullableInt64Of";
    public const string MaxInt64Of = "maxInt64Of";
    public const string MaxUInt64Of = "maxUInt64Of";
    public const string MaxNullableInt64Of = "maxNullableInt64Of";

    /// <summary>The range check of <c>long</c> (signed) or <c>ulong</c>.</summary>
    public static string CheckedRange(bool isSigned) => isSigned ? CheckedInt64 : CheckedUInt64;

    /// <summary><c>(long)d</c> / <c>(ulong)d</c>, saturating unchecked or throwing checked.</summary>
    public static string FromDouble(bool isSigned, bool isChecked) =>
        (isSigned, isChecked) switch
        {
            (true, false) => Int64FromDouble,
            (true, true) => Int64FromDoubleChecked,
            (false, false) => UInt64FromDouble,
            (false, true) => UInt64FromDoubleChecked,
        };

    /// <summary><c>(int)d</c> (FR-070).</summary>
    public static string Int32FromDouble(bool isChecked) =>
        isChecked ? Int32FromDoubleChecked : Int32FromDoubleUnchecked;

    /// <summary><c>(uint)d</c> (FR-070).</summary>
    public static string UInt32FromDouble(bool isChecked) =>
        isChecked ? UInt32FromDoubleChecked : UInt32FromDoubleUnchecked;

    /// <summary><c>(long)m</c> / <c>(ulong)m</c>, checked in every context.</summary>
    public static string FromDecimal(bool isSigned) =>
        isSigned ? Int64FromDecimal : UInt64FromDecimal;

    /// <summary><c>(long)b</c> / <c>(ulong)b</c>, checked in every context.</summary>
    public static string FromBigInteger(bool isSigned) =>
        isSigned ? Int64FromBigInteger : UInt64FromBigInteger;

    /// <summary><c>l / m</c> (signed) or <c>u / v</c>, throwing as .NET does.</summary>
    public static string Quotient(bool isSigned) => isSigned ? DivInt64 : DivUInt64;

    /// <summary><c>l % m</c> (signed) or <c>u % v</c>, throwing as .NET does.</summary>
    public static string Remainder(bool isSigned) => isSigned ? RemInt64 : RemUInt64;
}
