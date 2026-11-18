using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Types a value C# gives a numeric enum type but TypeScript computes as a
/// <c>number</c>. A plain numeric enum's type is the closed union of its member
/// values (spec 011 FR-044), which a <c>number</c> is not assignable to:
/// <list type="bullet">
///   <item>a cast to the enum (<c>(E)n</c>, <c>(E)3</c>) and the result of an
///   enum operator (<c>e + 1</c>, <c>a | b</c>, <c>~e</c>) are asserted,
///   <c>value as E</c>;</item>
///   <item>a compound assignment or an increment (<c>e += 1</c>, <c>e++</c>)
///   writes through its target asserted as a number,
///   <c>(e as number) += 1</c>, since TypeScript would otherwise narrow the
///   variable to <c>number</c> or reject the store.</item>
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
                Left = AsNumber(binary.Left),
            },
            TsUnaryExpression { Operator: "++" or "--" } increment => increment with
            {
                Operand = AsNumber(increment.Operand),
            },
            TsPostfixUnaryExpression increment => increment with
            {
                Operand = AsNumber(increment.Operand),
            },
            _ => new TsCastExpression(value, IrToTsTypeMapper.Map(type!)),
        };
    }

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
            is IrNamedTypeRef
            {
                Semantics: { Kind: IrNamedTypeKind.NumericEnum, IsFlagsEnum: false },
            };

    private static bool IsCompoundAssignment(string token) =>
        token.Length > 1
        && token[^1] == '='
        && token is not ("==" or "===" or "!=" or "!==" or "<=" or ">=");

    private static TsExpression AsNumber(TsExpression target) =>
        new TsCastExpression(target, new TsNumberType());
}
