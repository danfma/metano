using System;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The bitwise and shift rows of <c>uint</c> and <c>int</c> (spec 012
/// FR-076, issue #639): a <c>uint</c> result of <c>&amp;</c>, <c>|</c>,
/// <c>^</c>, <c>~</c> and <c>&lt;&lt;</c> is unsigned, <c>&gt;&gt;</c> on a
/// <c>uint</c> is a logical shift, <c>&gt;&gt;&gt;</c> on an <c>int</c>
/// yields an <c>int</c>, and a shift count is masked to 5 bits.
/// </summary>
[NoContainer]
public static class UnsignedBits
{
    public static uint UIntAnd(uint a, uint b) => a & b;

    public static int IntAnd(int a, int b) => a & b;

    public static uint UIntCompoundAnd(uint a, uint b)
    {
        a &= b;

        return a;
    }

    public static uint UIntOr(uint a, uint b) => a | b;

    public static int IntOr(int a, int b) => a | b;

    public static uint UIntCompoundOr(uint a, uint b)
    {
        a |= b;

        return a;
    }

    public static uint UIntXor(uint a, uint b) => a ^ b;

    public static int IntXor(int a, int b) => a ^ b;

    public static uint UIntCompoundXor(uint a, uint b)
    {
        a ^= b;

        return a;
    }

    public static uint UIntComplement(uint a) => ~a;

    public static int IntComplement(int a) => ~a;

    public static int UShortComplement(ushort a) => ~a;

    public static uint UIntShiftLeft(uint a, int count) => a << count;

    public static int IntShiftLeft(int a, int count) => a << count;

    public static uint UIntCompoundShiftLeft(uint a, int count)
    {
        a <<= count;

        return a;
    }

    public static int IntCompoundShiftLeft(int a, int count)
    {
        a <<= count;

        return a;
    }

    public static int SBytePromotedShiftLeft(sbyte a, int count) => a << count;

    public static int BytePromotedShiftLeft(byte a, int count) => a << count;

    public static uint UIntShiftRight(uint a, int count) => a >> count;

    public static int IntShiftRight(int a, int count) => a >> count;

    public static uint UIntCompoundShiftRight(uint a, int count)
    {
        a >>= count;

        return a;
    }

    public static int IntCompoundShiftRight(int a, int count)
    {
        a >>= count;

        return a;
    }

    public static int SBytePromotedShiftRight(sbyte a, int count) => a >> count;

    public static int BytePromotedShiftRight(byte a, int count) => a >> count;

    public static uint UIntShiftRightLogical(uint a, int count) => a >>> count;

    public static int IntShiftRightLogical(int a, int count) => a >>> count;

    public static uint UIntCompoundShiftRightLogical(uint a, int count)
    {
        a >>>= count;

        return a;
    }

    public static int IntCompoundShiftRightLogical(int a, int count)
    {
        a >>>= count;

        return a;
    }

    public static int SBytePromotedShiftRightLogical(sbyte a, int count) => a >>> count;

    public static int BytePromotedShiftRightLogical(byte a, int count) => a >>> count;

    public static uint UIntShiftLeftChecked(uint a, int count) => checked(a << count);

    public static uint UIntMaskLowByte(uint a) => a & 0xFF;

    public static uint UIntSetHighBit(uint a) => a | 0x80000000;

    public static uint UIntWithByte(uint a, byte b) => a & b;

    public static uint UIntMix(uint a, uint b) => ((a ^ (a >> 16)) << 5 | (b >> 27)) ^ ~b;

    public static uint? UIntLiftedOr(uint? a, uint? b) => a | b;

    public static uint? UIntLiftedComplement(uint? a) => ~a;

    public static uint UIntHighBit() => 1u << 31;

    public static uint UIntAllBits() => ~0u;

    public static uint UIntFieldCompound(uint start, uint mask)
    {
        var flags = new FlagSet(start);

        flags.Value |= mask;
        flags.Value <<= 1;
        flags.Value ^= mask;
        flags.Value >>= 3;
        flags.Value &= ~mask;

        return flags.Value;
    }
}

/// <summary>
/// The <c>uint</c> field <see cref="UnsignedBits.UIntFieldCompound"/> updates in
/// place. Emitted beside the functions that use it, so the generated module
/// has no relative import and runs under Node's type stripping as it is.
/// </summary>
[EmitInFile("unsigned-bits")]
public class FlagSet
{
    public uint Value;

    public FlagSet(uint value)
    {
        Value = value;
    }
}
