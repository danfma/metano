using System.Globalization;
using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using static Metano.Compiler.TypeScript.Bridge.Int64TsExpressions;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Renders an <see cref="IrNumericConversion"/> per spec 012
/// contracts/ts-emission.md § Conversions. A floating-point value cast to a
/// 32-bit or narrower integer truncates through the <c>metano-runtime</c>
/// helpers (FR-070). A narrowing between two integers of 32 bits or fewer
/// keeps the low bits with the native operators, or range-checks through a
/// helper in a checked context (FR-074, FR-077). Every other conversion
/// crosses the <c>bigint</c> a 64-bit integer is (FR-011–FR-019).
/// </summary>
internal static class IrToTsNumericConversionBridge
{
    private static readonly IrNumericEndpoint CodeUnit = new(IrNumericKind.UInt16);

    public static TsExpression Map(IrNumericConversion conversion, TsExpression operand)
    {
        if (conversion.IsLifted)
            return Lifted(conversion, operand);

        return FoldedCharLiteral(conversion) is { } code
            ? CharCodeAs(conversion.To, conversion.IsChecked, code)
            : RenderConversion(conversion.From, conversion.To, conversion.IsChecked, operand);
    }

    /// <summary>The conversion lifted over <c>null</c> (<see cref="NullLiftedConversion"/>).</summary>
    private static TsExpression Lifted(IrNumericConversion conversion, TsExpression operand) =>
        NullLiftedConversion.Map(conversion.Operand, operand, value =>
            RenderConversion(conversion.From, conversion.To, conversion.IsChecked, value)
        );

    private static TsExpression RenderConversion(
        IrNumericEndpoint declaredFrom,
        IrNumericEndpoint declaredTo,
        bool isChecked,
        TsExpression value
    )
    {
        var from = TsEnumRepresentation.Of(declaredFrom);
        var to = TsEnumRepresentation.Of(declaredTo);

        if (IrNumericConversion.IsFloatingPointTruncation(from, to))
            return FloatingPointToNarrowInteger(to, isChecked, value);

        if (IrNumericConversion.IsDecimalToBinaryFloatingPoint(from, to))
            return DecimalToBinaryFloatingPoint(to, value);

        if (IrNumericConversion.IsDecimalToNarrowInteger(from, to))
            return NarrowedDecimal(value, to.Kind.BitWidth(), to.Kind.IsSigned());

        if (IrNumericConversion.IsDecimalToChar(from, to))
            return Global(
                "String.fromCharCode",
                RuntimeCall(Int64RuntimeHelpers.CharCodeFromDecimal, value)
            );

        if (IrNumericConversion.IsCharRead(from, to))
            return CharCodeAs(to, isChecked, CharCode(value));

        if (IrNumericConversion.IsCharStore(from, to))
            return CharFromCode(isChecked, value);

        if (IrNumericConversion.IsIntegerToDecimal(from, to))
            return DecimalOfInteger(value);

        if (IrNumericConversion.IsDoubleToSingle(from, to))
            return Global("Math.fround", value);

        if (IrNumericConversion.IsBinaryFloatingPointToDecimal(from, to))
            return DecimalOfBinaryFloatingPoint(from, value);

        if (from is { Kind: IrNumericKind.BigInteger, IsEnum: false })
            return FromBigInteger(to, value);

        if (from.IsWideIntegerValue())
            return FromBigInt(from, to, isChecked, value);

        if (to.IsWideIntegerValue())
            return IntoBigInt(from, to, isChecked, value);

        if (!IrNumericConversion.IsIntegerNarrowing(from, to))
            return value;

        return isChecked
            ? NarrowIntegerTsExpressions.Checked(to.Kind, value)
            : NarrowIntegerTsExpressions.Wrapped(to.Kind, value);
    }

    /// <summary>
    /// <c>(double)m</c> reads the nearest <c>number</c> of the
    /// <c>Decimal</c>, <c>m.toNumber()</c>; <c>(float)m</c> rounds that to
    /// single precision, <c>Math.fround(m.toNumber())</c>.
    /// </summary>
    private static TsExpression DecimalToBinaryFloatingPoint(
        IrNumericEndpoint to,
        TsExpression value
    )
    {
        var asDouble = new TsCallExpression(new TsPropertyAccess(value, "toNumber"), []);

        return to.Kind is IrNumericKind.Single ? Global("Math.fround", asDouble) : asDouble;
    }

    /// <summary>
    /// Out of a <c>BigInteger</c> (FR-003): every explicit conversion checks
    /// its range in any context, through the <c>metano-runtime</c> helpers
    /// that reproduce .NET's results and messages; a <c>double</c> keeps
    /// .NET's truncated significand.
    /// </summary>
    private static TsExpression FromBigInteger(IrNumericEndpoint to, TsExpression value) =>
        to.Kind switch
        {
            IrNumericKind.BigInteger => value,
            IrNumericKind.Int64 or IrNumericKind.UInt64 => RuntimeCall(
                Int64RuntimeHelpers.FromBigInteger(to.Kind.IsSigned()),
                value
            ),
            IrNumericKind.Int32 => RuntimeCall(Int64RuntimeHelpers.Int32FromBigInteger, value),
            IrNumericKind.UInt32 => RuntimeCall(Int64RuntimeHelpers.UInt32FromBigInteger, value),
            IrNumericKind.Char => Global(
                "String.fromCharCode",
                NarrowedBigInteger(value, bits: 16, signed: false)
            ),
            IrNumericKind.Double => RuntimeCall(Int64RuntimeHelpers.DoubleFromBigInteger, value),
            IrNumericKind.Single => Global(
                "Math.fround",
                RuntimeCall(Int64RuntimeHelpers.DoubleFromBigInteger, value)
            ),
            IrNumericKind.Decimal => new TsNewExpression(
                new TsIdentifier("Decimal"),
                [RuntimeCall(Int64RuntimeHelpers.DecimalTextFromBigInteger, value)]
            ),
            _ => NarrowedBigInteger(value, to.Kind.BitWidth(), to.Kind.IsSigned()),
        };

    private static TsExpression NarrowedDecimal(TsExpression value, int bits, bool signed) =>
        RuntimeCall(
            Int64RuntimeHelpers.NarrowFromDecimal,
            value,
            new TsLiteral(bits.ToString(CultureInfo.InvariantCulture)),
            new TsLiteral(signed ? "true" : "false")
        );

    private static TsExpression NarrowedBigInteger(TsExpression value, int bits, bool signed) =>
        RuntimeCall(
            Int64RuntimeHelpers.NarrowFromBigInteger,
            value,
            new TsLiteral(bits.ToString(CultureInfo.InvariantCulture)),
            new TsLiteral(signed ? "true" : "false")
        );

    /// <summary>Out of a 64-bit <c>bigint</c> (<c>long</c>, <c>ulong</c>).</summary>
    private static TsExpression FromBigInt(
        IrNumericEndpoint from,
        IrNumericEndpoint to,
        bool isChecked,
        TsExpression value
    ) =>
        to.Kind switch
        {
            IrNumericKind.BigInteger => value,
            IrNumericKind.Int64 or IrNumericKind.UInt64 => BetweenSixtyFourBits(
                from,
                to,
                isChecked,
                value
            ),
            IrNumericKind.Char => Global(
                "String.fromCharCode",
                Number(Narrowed(value, bits: 16, signed: false, isChecked))
            ),
            IrNumericKind.Double => Number(value),
            IrNumericKind.Single => RuntimeCall(Int64RuntimeHelpers.Int64ToSingle, value),
            IrNumericKind.Decimal => new TsNewExpression(
                IrToTsExpressionBridge.BclDecimalValueReference(),
                [new TsCallExpression(new TsPropertyAccess(value, "toString"), [])]
            ),
            _ => Number(Narrowed(value, to.Kind.BitWidth(), to.Kind.IsSigned(), isChecked)),
        };

    /// <summary>
    /// <c>long</c> ↔ <c>ulong</c> keeps the 64 bits unchecked and range-checks
    /// checked (FR-013); <c>BigInteger</c> → 64 bits is checked in every
    /// context (FR-017).
    /// </summary>
    private static TsExpression BetweenSixtyFourBits(
        IrNumericEndpoint from,
        IrNumericEndpoint to,
        bool isChecked,
        TsExpression value
    )
    {
        var isSigned = to.Kind.IsSigned();

        if (from.Kind is IrNumericKind.BigInteger)
            return RuntimeCall(Int64RuntimeHelpers.FromBigInteger(isSigned), value);

        if (from.Kind == to.Kind)
            return value;

        return isChecked
            ? RuntimeCall(Int64RuntimeHelpers.CheckedRange(isSigned), value)
            : AsIntN(isSigned, 64, value);
    }

    /// <summary>Into a <c>bigint</c> from a <c>number</c>, a <c>char</c> or a <c>Decimal</c>.</summary>
    private static TsExpression IntoBigInt(
        IrNumericEndpoint from,
        IrNumericEndpoint to,
        bool isChecked,
        TsExpression value
    )
    {
        if (to.Kind is IrNumericKind.BigInteger && BigIntegerFromFraction(from.Kind) is { } helper)
            return RuntimeCall(helper, value);

        // BigInteger counts as signed: only a ulong target picks the
        // unsigned helper.
        var isSigned = to.Kind is not IrNumericKind.UInt64;

        return from.Kind switch
        {
            IrNumericKind.Double or IrNumericKind.Single => RuntimeCall(
                Int64RuntimeHelpers.FromDouble(isSigned, isChecked),
                value
            ),
            IrNumericKind.Decimal => RuntimeCall(Int64RuntimeHelpers.FromDecimal(isSigned), value),
            _ => WidenedInteger(from, to, isChecked, value),
        };
    }

    /// <summary>The UTF-16 code of a <c>char</c>, which is a one-unit string: <c>value.charCodeAt(0)</c>.</summary>
    private static TsExpression CharCode(TsExpression value) =>
        new TsCallExpression(new TsPropertyAccess(value, "charCodeAt"), [new TsLiteral("0")]);

    /// <summary>
    /// A relational or equality operator whose two operands are non-nullable
    /// <c>char</c> reads (<c>c == 'a'</c>, <c>a &lt; b</c>) compares them as
    /// the one-unit strings they are, since a JS string comparison orders by
    /// UTF-16 code unit as .NET orders <c>char</c> (FR-071); the operands lose
    /// their conversion, unless the target cannot hold every code unit
    /// (<c>(byte)c</c> keeps the low bits). A nullable operand keeps its lifted
    /// conversion.
    /// </summary>
    public static IrBinaryExpression WithCharOperandsAsText(IrBinaryExpression binary) =>
        binary.Operator
            is IrBinaryOp.Equal
                or IrBinaryOp.NotEqual
                or IrBinaryOp.LessThan
                or IrBinaryOp.LessThanOrEqual
                or IrBinaryOp.GreaterThan
                or IrBinaryOp.GreaterThanOrEqual
        && binary is { Left: IrNumericConversion left, Right: IrNumericConversion right }
        && IsPlainCharRead(left)
        && IsPlainCharRead(right)
            ? binary with
            {
                Left = left.Operand,
                Right = right.Operand,
            }
            : binary;

    private static bool IsPlainCharRead(IrNumericConversion conversion) =>
        !conversion.IsLifted
        && IrNumericConversion.IsCharRead(conversion.From, conversion.To)
        && !IrNumericConversion.IsIntegerNarrowing(CodeUnit, conversion.To);

    /// <summary>
    /// The UTF-16 <paramref name="code"/> of a <c>char</c> converted to
    /// <paramref name="to"/>: a <c>ushort</c> read like any other integer, a
    /// <c>decimal</c> as a <c>Decimal</c> value.
    /// </summary>
    private static TsExpression CharCodeAs(
        IrNumericEndpoint to,
        bool isChecked,
        TsExpression code
    ) =>
        to.Kind is IrNumericKind.Decimal
            ? DecimalOf(code)
            : RenderConversion(CodeUnit, to, isChecked, code);

    /// <summary>
    /// A <c>Decimal</c> of an integer <c>number</c> of 32 bits or fewer, exact
    /// for every such value (FR-080): <c>new Decimal(value + 0)</c>. A JS
    /// integer can be <c>-0</c> (<c>-4 % 2</c>, <c>-4 * 0</c>), which a C#
    /// integer never is, and the <c>Decimal</c> keeps its sign
    /// (<c>"" + new Decimal(-0)</c> is <c>"-0"</c>); adding <c>0</c> turns it
    /// into <c>0</c> and changes no other value. A literal needs nothing.
    /// </summary>
    private static TsExpression DecimalOfInteger(TsExpression value) =>
        DecimalOf(
            value is TsLiteral ? value : new TsBinaryExpression(value, "+", new TsLiteral("0"))
        );

    /// <summary>
    /// <c>(decimal)d</c> / <c>(decimal)f</c>: the value rounded to the 15 /
    /// 7 significant digits .NET keeps, <c>new Decimal(decimalTextFromDouble(d, 15))</c>.
    /// </summary>
    private static TsExpression DecimalOfBinaryFloatingPoint(
        IrNumericEndpoint from,
        TsExpression value
    )
    {
        var digits = from.Kind is IrNumericKind.Single ? "7" : "15";

        return DecimalOf(
            RuntimeCall(Int64RuntimeHelpers.DecimalTextFromDouble, value, new TsLiteral(digits))
        );
    }

    /// <summary>A <c>Decimal</c> of a <c>number</c>: <c>new Decimal(value)</c>.</summary>
    private static TsExpression DecimalOf(TsExpression value) =>
        new TsNewExpression(IrToTsExpressionBridge.BclDecimalValueReference(), [value]);

    /// <summary>
    /// The code of a <c>char</c> literal read as a number (FR-071), which the
    /// bridge folds instead of printing <c>"a".charCodeAt(0)</c>; <c>null</c>
    /// for any other conversion.
    /// </summary>
    private static TsLiteral? FoldedCharLiteral(IrNumericConversion conversion) =>
        IrNumericConversion.IsCharRead(conversion.From, conversion.To)
        && conversion.Operand is IrLiteral { Kind: IrLiteralKind.Char, Value: char character }
            ? new TsLiteral(((int)character).ToString(CultureInfo.InvariantCulture))
            : null;

    /// <summary>
    /// A <c>char</c> made of an integer of 32 bits or fewer (FR-072): the low
    /// 16 bits unchecked, a range check checked, then <c>String.fromCharCode</c>.
    /// </summary>
    private static TsExpression CharFromCode(bool isChecked, TsExpression value) =>
        Global(
            "String.fromCharCode",
            isChecked
                ? NarrowIntegerTsExpressions.Checked(IrNumericKind.Char, value)
                : NarrowIntegerTsExpressions.Wrapped(IrNumericKind.Char, value)
        );

    /// <summary>The helper that truncates a fraction into a <c>BigInteger</c> (FR-003), or <c>null</c>.</summary>
    private static string? BigIntegerFromFraction(IrNumericKind from) =>
        from switch
        {
            IrNumericKind.Double or IrNumericKind.Single =>
                Int64RuntimeHelpers.BigIntegerFromDouble,
            IrNumericKind.Decimal => Int64RuntimeHelpers.BigIntegerFromDecimal,
            _ => null,
        };

    /// <summary>
    /// An integer of 32 bits or fewer is exact as a <c>bigint</c>; a signed
    /// source read as <c>ulong</c> wraps unchecked and throws checked when
    /// negative (FR-011, FR-013).
    /// </summary>
    private static TsExpression WidenedInteger(
        IrNumericEndpoint from,
        IrNumericEndpoint to,
        bool isChecked,
        TsExpression value
    )
    {
        var widened = BigInt(value);
        var reinterpretsNegative = to.Kind is IrNumericKind.UInt64 && from.Kind.IsSigned();

        if (!reinterpretsNegative)
            return widened;

        return isChecked
            ? RuntimeCall(Int64RuntimeHelpers.CheckedRange(isSigned: false), widened)
            : AsIntN(isSigned: false, 64, widened);
    }

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
            return RuntimeCall(Int64RuntimeHelpers.UInt32FromDouble(isChecked), value);

        var asInt32 = RuntimeCall(Int64RuntimeHelpers.Int32FromDouble(isChecked), value);

        var narrowed = (to.Kind, isChecked) switch
        {
            (IrNumericKind.Int32, _) => asInt32,
            (_, true) => Number(
                Narrowed(BigInt(asInt32), to.Kind.BitWidth(), to.Kind.IsSigned(), isChecked: true)
            ),
            (_, false) => NarrowIntegerTsExpressions.Wrapped(to.Kind, asInt32),
        };

        return to.Kind is IrNumericKind.Char ? Global("String.fromCharCode", narrowed) : narrowed;
    }

    private static TsExpression Narrowed(TsExpression value, int bits, bool signed, bool isChecked)
    {
        var width = new TsLiteral(bits.ToString(CultureInfo.InvariantCulture));

        return isChecked
            ? RuntimeCall(
                Int64RuntimeHelpers.CheckedNarrow,
                value,
                width,
                new TsLiteral(signed ? "true" : "false")
            )
            : AsIntN(signed, bits, value);
    }
}
