namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The names of the <c>metano-runtime/system/int64</c> helpers the TypeScript
/// bridge emits (spec 012 contracts/runtime-int64.md), the single place they
/// are spelled, with the selectors that pick a variant by context.
/// </summary>
internal static class Int64RuntimeHelpers
{
    public const string CheckedNarrow = "checkedNarrow";
    public const string Int32FromDoubleUnchecked = "int32FromDouble";
    public const string Int32FromDoubleChecked = "int32FromDoubleChecked";
    public const string UInt32FromDoubleUnchecked = "uint32FromDouble";
    public const string UInt32FromDoubleChecked = "uint32FromDoubleChecked";

    /// <summary><c>(int)d</c> (FR-070).</summary>
    public static string Int32FromDouble(bool isChecked) =>
        isChecked ? Int32FromDoubleChecked : Int32FromDoubleUnchecked;

    /// <summary><c>(uint)d</c> (FR-070).</summary>
    public static string UInt32FromDouble(bool isChecked) =>
        isChecked ? UInt32FromDoubleChecked : UInt32FromDoubleUnchecked;
}
