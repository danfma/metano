namespace Conformance;

/// <summary>Spec 011 FR-044: #708 review follow-ups and conversion side effects.</summary>
public static class EnumFollowupProbes
{
    private static C8Byte DecimalByte(decimal value) => unchecked((C8Byte)value);

    private static C8SByte DecimalSByte(decimal value) => unchecked((C8SByte)value);

    private static C8Short DecimalShort(decimal value) => unchecked((C8Short)value);

    private static C8UShort DecimalUShort(decimal value) => unchecked((C8UShort)value);

    private static C8Int DecimalInt(decimal value) => unchecked((C8Int)value);

    private static C8UInt DecimalUInt(decimal value) => unchecked((C8UInt)value);

    private static C8Byte CheckedDecimalByte(decimal value) => checked((C8Byte)value);

    public static string DecimalTruncation() =>
        (byte)DecimalByte(255.9m)
        + ","
        + (sbyte)DecimalSByte(-128.9m)
        + ","
        + (short)DecimalShort(-32768.9m)
        + ","
        + (ushort)DecimalUShort(65535.9m)
        + ","
        + (int)DecimalInt(2147483647.9m)
        + ","
        + (uint)DecimalUInt(4294967295.9m);

    public static string DecimalByteOverflow() => ((byte)DecimalByte(256m)).ToString();

    public static string DecimalSByteOverflow() => ((sbyte)DecimalSByte(-129m)).ToString();

    public static string DecimalShortOverflow() => ((short)DecimalShort(-32769m)).ToString();

    public static string DecimalUShortOverflow() => ((ushort)DecimalUShort(65536m)).ToString();

    public static string DecimalIntOverflow() => ((int)DecimalInt(2147483648m)).ToString();

    public static string DecimalUIntOverflow() => ((uint)DecimalUInt(-1m)).ToString();

    public static string DecimalCheckedOverflow() => ((byte)CheckedDecimalByte(256m)).ToString();

    public static string DecimalNegativeFraction() => ((byte)DecimalByte(-0.9m)).ToString();

    private static C8Byte? NullableDecimal(decimal? value) => unchecked((C8Byte?)value);

    public static string DecimalNullableNull() => (NullableDecimal(null) is null).ToString();

    public static string DecimalNullableTruncation() =>
        ((byte)NullableDecimal(42.9m)!.Value).ToString();

    public static string DecimalNullableOverflow() =>
        ((byte)NullableDecimal(256m)!.Value).ToString();

    private static C8Byte Add(C8Byte value, byte step) => unchecked(value + step);

    private static C8Byte Subtract(C8Byte value, byte step) => unchecked(value - step);

    private static C8Byte CheckedAdd(C8Byte value, byte step) => checked(value + step);

    private static C8Byte CheckedSubtract(C8Byte value, byte step) => checked(value - step);

    private static C8SByte AddSByte(C8SByte value, sbyte step) => unchecked(value + step);

    private static C8Short AddShort(C8Short value, short step) => unchecked(value + step);

    private static C8UShort AddUShort(C8UShort value, ushort step) => unchecked(value + step);

    private static C8Int AddInt(C8Int value, int step) => unchecked(value + step);

    private static C8UInt AddUInt(C8UInt value, uint step) => unchecked(value + step);

    private static C8SByte CheckSByte(C8SByte value, sbyte step) => checked(value + step);

    private static C8Short CheckShort(C8Short value, short step) => checked(value + step);

    private static C8UShort CheckUShort(C8UShort value, ushort step) => checked(value + step);

    private static C8Int CheckInt(C8Int value, int step) => checked(value + step);

    private static C8UInt CheckUInt(C8UInt value, uint step) => checked(value + step);

    public static string ArithmeticByteWrap() =>
        (byte)Add(DecimalByte(255m), 1) + "," + (byte)Subtract(DecimalByte(0m), 1);

    public static string ArithmeticSByteWrap() =>
        ((sbyte)AddSByte(DecimalSByte(127m), 1)).ToString();

    public static string ArithmeticShortWrap() =>
        ((short)AddShort(DecimalShort(32767m), 1)).ToString();

    public static string ArithmeticUShortWrap() =>
        ((ushort)AddUShort(DecimalUShort(65535m), 1)).ToString();

    public static string ArithmeticIntWrap() =>
        ((int)AddInt(DecimalInt(2147483647m), 1)).ToString();

    public static string ArithmeticUIntWrap() =>
        ((uint)AddUInt(DecimalUInt(4294967295m), 1)).ToString();

    public static string ArithmeticByteOverflow() =>
        ((byte)CheckedAdd(DecimalByte(255m), 1)).ToString();

    public static string ArithmeticSubtractOverflow() =>
        ((byte)CheckedSubtract(DecimalByte(0m), 1)).ToString();

    public static string ArithmeticSByteOverflow() =>
        ((sbyte)CheckSByte(DecimalSByte(127m), 1)).ToString();

    public static string ArithmeticShortOverflow() =>
        ((short)CheckShort(DecimalShort(32767m), 1)).ToString();

    public static string ArithmeticUShortOverflow() =>
        ((ushort)CheckUShort(DecimalUShort(65535m), 1)).ToString();

    public static string ArithmeticIntOverflow() =>
        ((int)CheckInt(DecimalInt(2147483647m), 1)).ToString();

    public static string ArithmeticUIntOverflow() =>
        ((uint)CheckUInt(DecimalUInt(4294967295m), 1)).ToString();

    public static string ArithmeticInRange() => ((byte)CheckedAdd(DecimalByte(254m), 1)).ToString();

    public static string PostfixWrap()
    {
        var value = DecimalByte(255m);
        var previous = value++;
        return (byte)previous + "," + (byte)value;
    }

    public static string PrefixWrap()
    {
        var value = DecimalByte(0m);
        var current = --value;
        return (byte)current + "," + (byte)value;
    }

    public static string IncrementChecked()
    {
        var value = DecimalByte(255m);
        checked
        {
            value++;
        }
        return ((byte)value).ToString();
    }

    public static string CompoundWrap()
    {
        var value = DecimalByte(255m);
        value += 1;
        value -= 1;
        return ((byte)value).ToString();
    }

    public static string CompoundChecked()
    {
        var value = DecimalByte(255m);
        checked
        {
            value += 1;
        }
        return ((byte)value).ToString();
    }

    public static string LiftedArithmetic()
    {
        C8Byte? value = DecimalByte(255m);
        var previous = value++;
        value += 1;
        C8Byte? missing = null;
        missing++;
        return (byte)previous!.Value + "," + (byte)value!.Value + "," + (missing is null);
    }

    private static char Char(C8Int value) => unchecked((char)value);

    private static C8Byte FromChar(char value) => unchecked((C8Byte)value);

    private static C8Byte CheckedChar(char value) => checked((C8Byte)value);

    private static decimal ToDecimal(C8UInt value) => (decimal)value;

    public static string EnumToChar() => Char(DecimalInt(65601m)).ToString();

    public static string CharToEnum() => ((byte)FromChar('\u0141')).ToString();

    public static string CheckedCharToEnum() => ((byte)CheckedChar('\u0141')).ToString();

    public static string EnumToDecimal() => ToDecimal(DecimalUInt(4294967295m)).ToString();

    private static (C8Byte, C8SByte) Tuple((int, int) value) => unchecked(((C8Byte, C8SByte))value);

    private static (C8Byte, C8SByte) CheckedTuple((int, int) value) =>
        checked(((C8Byte, C8SByte))value);

    public static string TupleEnumWrap()
    {
        var pair = Tuple((-1, 128));
        return (byte)pair.Item1 + "," + (sbyte)pair.Item2;
    }

    public static string TupleEnumChecked() => ((byte)CheckedTuple((-1, 128)).Item1).ToString();

    public static string NullableForeachWrap()
    {
        int?[] values = [-1, null, 256];
        var result = "";
        foreach (C8Byte? value in values)
            result += (value is null ? "null" : ((byte)value.Value).ToString()) + ",";
        return result;
    }

    public static string ProjectCheckedAdd() =>
        ((byte)CheckedEnums.EnumCasts.Add(CheckedEnums.EnumCasts.Byte(255), 1)).ToString();

    public static string ProjectUncheckedAdd() =>
        ((byte)CheckedEnums.EnumCasts.AddUnchecked(CheckedEnums.EnumCasts.Byte(255), 1)).ToString();

    public static string ProjectCheckedIncrement() =>
        ((byte)CheckedEnums.EnumCasts.Increment(CheckedEnums.EnumCasts.Byte(255))).ToString();

    public static string ProjectCheckedCompound() =>
        ((byte)CheckedEnums.EnumCasts.Compound(CheckedEnums.EnumCasts.Byte(255), 1)).ToString();

    private static decimal? NextDecimal(decimal? value, int[] calls)
    {
        calls[0]++;
        return value;
    }

    private static string DecimalOnce(decimal? value)
    {
        var calls = new int[1];
        var result = unchecked((C8Byte?)NextDecimal(value, calls));
        return (result is null ? "null" : ((byte)result.Value).ToString()) + "," + calls[0];
    }

    public static string DecimalEvaluatedOnce() => DecimalOnce(42.9m);

    public static string DecimalNullEvaluatedOnce() => DecimalOnce(null);

    private static int Index(int[] calls)
    {
        calls[0]++;
        return 0;
    }

    public static string PostfixIndexEvaluatedOnce()
    {
        C8Byte[] values = [DecimalByte(255m)];
        var calls = new int[1];
        var previous = values[Index(calls)]++;
        return (byte)previous + "," + (byte)values[0] + "," + calls[0];
    }

    public static string CheckedCompoundPreservesStore()
    {
        var value = DecimalByte(255m);

        try
        {
            checked
            {
                value += 1;
            }
            return "no throw";
        }
        catch (OverflowException)
        {
            return ((byte)value).ToString();
        }
    }
}
