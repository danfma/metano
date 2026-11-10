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
    private const string LiftedValueName = "value";

    public static TsExpression Map(IrNumericConversion conversion, TsExpression operand) =>
        conversion.IsLifted
            ? Lifted(conversion, operand)
            : RenderConversion(conversion.From, conversion.To, conversion.IsChecked, operand);

    /// <summary>
    /// <c>x == null ? null : convert(x)</c> for an operand that is safe to
    /// read twice, <c>mapNullable(x, (value) =&gt; convert(value))</c>
    /// otherwise, so the operand is evaluated once (FR-019). The loose
    /// <c>==</c> also catches the <c>undefined</c> of an optional field.
    /// </summary>
    private static TsExpression Lifted(IrNumericConversion conversion, TsExpression operand)
    {
        var (from, to, isChecked) = (conversion.From, conversion.To, conversion.IsChecked);

        if (IrEvaluationTraits.IsSideEffectFree(conversion.Operand))
        {
            return new TsConditionalExpression(
                new TsBinaryExpression(operand, "==", new TsLiteral("null")),
                new TsLiteral("null"),
                RenderConversion(from, to, isChecked, operand)
            );
        }

        var value = new TsIdentifier(LiftedValueName);

        var convert = new TsArrowFunction(
            [new TsParameter(LiftedValueName, null)],
            [new TsReturnStatement(RenderConversion(from, to, isChecked, value))]
        );

        return RuntimeCall(Int64RuntimeHelpers.MapNullable, operand, convert);
    }

    private static TsExpression RenderConversion(
        IrNumericEndpoint from,
        IrNumericEndpoint to,
        bool isChecked,
        TsExpression value
    )
    {
        if (IrNumericConversion.IsFloatingPointTruncation(from, to))
            return FloatingPointToNarrowInteger(to, isChecked, value);

        if (to.IsEnum)
            return ToEnum(from, to, isChecked, value);

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
    /// An enum value is a <c>number</c>, whatever its underlying kind
    /// (DECIDED-15): a conversion to an enum converts to that kind and, for a
    /// 64-bit kind, reads the result back as a <c>number</c>; an integer
    /// <c>number</c> needs nothing (FR-018).
    /// </summary>
    private static TsExpression ToEnum(
        IrNumericEndpoint from,
        IrNumericEndpoint to,
        bool isChecked,
        TsExpression value
    )
    {
        var underlying = new IrNumericEndpoint(to.Kind);

        if (!underlying.IsWideIntegerValue())
            return RenderConversion(from, underlying, isChecked, value);

        var isIntegerNumber = !from.IsWideIntegerValue() && from.Kind.IsInteger();

        return isIntegerNumber
            ? value
            : Number(RenderConversion(from, underlying, isChecked, value));
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
            IrNumericKind.Char => BigInt(
                new TsCallExpression(
                    new TsPropertyAccess(value, "charCodeAt"),
                    [new TsLiteral("0")]
                )
            ),
            _ => WidenedInteger(from, to, isChecked, value),
        };
    }

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
