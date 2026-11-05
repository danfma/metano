using System.Globalization;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Renders an <see cref="IrNumericConversion"/> per spec 012
/// contracts/ts-emission.md § Conversions. A floating-point value cast to a
/// 32-bit or narrower integer truncates through the <c>metano-runtime</c>
/// helpers in both 64-bit modes (FR-070); the later slices of the 64-bit
/// lowering render the other conversions here.
/// </summary>
internal static class IrToTsNumericConversionBridge
{
    public static TsExpression Map(IrNumericConversion conversion, TsExpression operand) =>
        conversion.TruncatesFloatingPoint
            ? FloatingPointToNarrowInteger(conversion.To, conversion.IsChecked, operand)
            : operand;

    /// <summary>
    /// FR-070: truncate and saturate to <c>int</c> (or <c>uint</c>), then keep
    /// the low bits for a narrower target, as .NET does; a checked cast throws
    /// outside the target's range.
    /// </summary>
    private static TsExpression FloatingPointToNarrowInteger(
        IrNumericEndpoint to,
        bool isChecked,
        TsExpression value
    )
    {
        if (to.Kind is IrNumericKind.UInt32)
            return Helper(Int64RuntimeHelpers.UInt32FromDouble(isChecked), value);

        var asInt32 = Helper(Int64RuntimeHelpers.Int32FromDouble(isChecked), value);

        var narrowed = (to.Kind, isChecked) switch
        {
            (IrNumericKind.Int32, _) => asInt32,
            (_, true) => Number(
                Narrowed(BigInt(asInt32), to.Kind.BitWidth(), to.Kind.IsSigned(), isChecked: true)
            ),
            (IrNumericKind.SByte, false) => SignExtended(asInt32, 24),
            (IrNumericKind.Int16, false) => SignExtended(asInt32, 16),
            (IrNumericKind.Byte, false) => LowBits(asInt32, "0xFF"),
            (IrNumericKind.UInt16 or IrNumericKind.Char, false) => LowBits(asInt32, "0xFFFF"),
            _ => throw new InvalidOperationException(
                $"'{to.Kind}' is not a narrow integer target of a floating-point truncation."
            ),
        };

        return to.Kind is IrNumericKind.Char ? Global("String.fromCharCode", narrowed) : narrowed;
    }

    private static TsExpression LowBits(TsExpression value, string mask) =>
        new TsBinaryExpression(value, "&", new TsLiteral(mask));

    /// <summary><c>(value &lt;&lt; shift) &gt;&gt; shift</c>: the low bits, sign-extended.</summary>
    private static TsExpression SignExtended(TsExpression value, int shift)
    {
        var count = new TsLiteral(shift.ToString(CultureInfo.InvariantCulture));

        return new TsBinaryExpression(
            new TsParenthesized(new TsBinaryExpression(value, "<<", count)),
            ">>",
            count
        );
    }

    private static TsExpression Narrowed(TsExpression value, int bits, bool signed, bool isChecked)
    {
        var width = new TsLiteral(bits.ToString(CultureInfo.InvariantCulture));

        return isChecked
            ? Helper(
                Int64RuntimeHelpers.CheckedNarrow,
                value,
                width,
                new TsLiteral(signed ? "true" : "false")
            )
            : AsIntN(signed, bits, value);
    }

    private static TsExpression AsIntN(bool isSigned, int bits, TsExpression value) =>
        Global(
            isSigned ? "BigInt.asIntN" : "BigInt.asUintN",
            new TsLiteral(bits.ToString(CultureInfo.InvariantCulture)),
            value
        );

    private static TsExpression BigInt(TsExpression value) => Global("BigInt", value);

    private static TsExpression Number(TsExpression value) => Global("Number", value);

    /// <summary>A call of a <c>metano-runtime</c> helper, imported through its reference node.</summary>
    private static TsCallExpression Helper(string name, params TsExpression[] arguments) =>
        new(new TsRuntimeHelperReference(name), arguments);

    /// <summary>
    /// A call of a JavaScript global; a dotted name (<c>BigInt.asIntN</c>)
    /// becomes a property access.
    /// </summary>
    private static TsCallExpression Global(string function, params TsExpression[] arguments)
    {
        var dot = function.IndexOf('.');

        TsExpression callee =
            dot < 0
                ? new TsIdentifier(function)
                : new TsPropertyAccess(new TsIdentifier(function[..dot]), function[(dot + 1)..]);

        return new TsCallExpression(callee, arguments);
    }
}
