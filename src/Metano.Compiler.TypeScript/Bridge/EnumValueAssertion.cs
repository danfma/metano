using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Types a value C# gives a numeric enum type but TypeScript computes as a
/// <c>number</c>. A plain numeric enum's type is the closed union of its member
/// values (spec 011 FR-044), which a <c>number</c> is not assignable to:
/// <list type="bullet">
///   <item>a cast to the enum (<c>(E)n</c>, <c>(E)3</c>), a constant <c>0</c>
///   C# converts to the enum implicitly (<c>E e = 0;</c>), and the result of an
///   enum operator (<c>e + 1</c>, <c>a | b</c>, <c>~e</c>) are asserted,
///   <c>value as E</c>;</item>
///   <item>a compound assignment or an increment (<c>e += 1</c>, <c>e++</c>)
///   writes through its target asserted as a number,
///   <c>(e as number) += 1</c> (<c>(e as bigint) |= E.B</c> for a 64-bit enum,
///   whose reduced stores are asserted whole, <see cref="OfReduced"/>), since
///   TypeScript would otherwise narrow the variable or reject the store.</item>
/// </list>
/// A local of such an enum asserts its initializer (<c>let s = E.A as E;</c>):
/// TypeScript would otherwise infer or narrow the local to the member's literal
/// type, which neither an increment nor a comparison with another member
/// accepts.
/// A <c>[Flags]</c> enum's brand takes any <c>number</c>, and every other type
/// needs nothing. A nullable enum (<c>(E?)n</c>) is asserted as <c>E | null</c>.
/// </summary>
internal static class EnumValueAssertion
{
    public static TsExpression Wrap(TsExpression value, IrTypeRef? type)
    {
        if (!IsClosedNumericEnum(type))
            return value;

        return value switch
        {
            TsBinaryExpression binary when IsCompoundAssignment(binary.Operator) => binary with
            {
                Left = AsUnderlying(binary.Left, type!),
            },
            TsUnaryExpression { Operator: "++" or "--" } increment => increment with
            {
                Operand = AsUnderlying(increment.Operand, type!),
            },
            TsPostfixUnaryExpression increment => increment with
            {
                Operand = AsUnderlying(increment.Operand, type!),
            },
            _ => new TsCastExpression(value, IrToTsTypeMapper.Map(type!)),
        };
    }

    /// <summary>
    /// The reduction of a numeric enum's operator (<c>BigInt.asIntN(64, e + 1n)</c>,
    /// #701; <c>(e + 1) &amp; 0xFF</c>, #708) asserted as a whole, so the enum
    /// type its operator carries moves from the operator to the reduction.
    /// </summary>
    public static TsExpression OfReduced(
        IrIntegerOverflow overflow,
        Func<IrIntegerOverflow, TsExpression> map
    ) =>
        IrEnumResultTypes.Of(overflow.Value) is { } type
            ? Wrap(map((IrIntegerOverflow)IrEnumResultTypes.With(overflow, null)), type)
            : map(overflow);

    /// <summary>
    /// <paramref name="value"/> asserted as the enum <paramref name="type"/>, unless it
    /// already is an assertion.
    /// </summary>
    public static TsExpression AsEnum(TsExpression value, IrTypeRef type) =>
        value is TsCastExpression ? value : new TsCastExpression(value, IrToTsTypeMapper.Map(type));

    /// <summary>
    /// Whether <paramref name="type"/> (or its non-nullable form) is a plain numeric
    /// enum, whose TypeScript type is the closed union of its member values.
    /// </summary>
    public static bool IsClosedNumericEnum(IrTypeRef? type) =>
        (type is IrNullableTypeRef nullable ? nullable.Inner : type)
            is IrNamedTypeRef { Semantics: { Kind: IrNamedTypeKind.NumericEnum } semantics }
        && !IrToTsEnumBridge.HasFlagsBrand(isNumeric: true, semantics.IsFlagsEnum);

    private static bool IsCompoundAssignment(string token) =>
        token.Length > 1
        && token[^1] == '='
        && token is not ("==" or "===" or "!=" or "!==" or "<=" or ">=");

    /// <summary><paramref name="target"/> asserted as the enum's underlying TS type: <c>number</c>, or <c>bigint</c> for a 64-bit enum.</summary>
    private static TsExpression AsUnderlying(TsExpression target, IrTypeRef type) =>
        new TsCastExpression(
            target,
            TsEnumRepresentation.IsBigInt(type) ? new TsBigIntType() : new TsNumberType()
        );
}
