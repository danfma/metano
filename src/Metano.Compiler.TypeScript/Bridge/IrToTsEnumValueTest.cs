using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one predicate that decides whether a value is a value of an enum (spec
/// 011 FR-049 as decided for 3.0): every runtime test of an enum renders it —
/// a type pattern, <c>is</c>, an overload dispatcher arm, and the
/// <c>test</c> of the enum's descriptor literal, through which a cast
/// (<c>castTo</c>) and <c>as</c> (<c>asType</c>) check it. A numeric enum,
/// <c>[Flags]</c> included, accepts any integral number in its underlying
/// type's range, as .NET accepts <c>(Color)7</c>; a <c>[StringEnum]</c>
/// accepts its declared strings. Membership of a numeric enum's declared
/// values is <c>Enum.IsDefined</c>, not a type test. Changing the semantics
/// is a change to this class only. The test reads no descriptor and imports
/// at most a validating check.
/// </summary>
internal static class IrToTsEnumValueTest
{
    /// <summary>The test that <paramref name="value"/> is a value of <paramref name="enumType"/>.</summary>
    public static TsExpression Of(TsExpression value, IrNamedTypeRef enumType) =>
        enumType.Semantics?.Kind is IrNamedTypeKind.StringEnum
            ? StringEnumTest(value, enumType.Semantics.StringEnumValues)
            : IntegralTest(value, enumType.Semantics?.EnumUnderlyingKind ?? IrNumericKind.Int32);

    /// <summary>
    /// The declared strings; any string when the reference does not carry them.
    /// </summary>
    private static TsExpression StringEnumTest(TsExpression value, IReadOnlyList<string>? values) =>
        values is { Count: > 0 }
            ? values
                .Select(member =>
                    (TsExpression)new TsBinaryExpression(value, "===", new TsStringLiteral(member))
                )
                .Aggregate((left, right) => new TsBinaryExpression(left, "||", right))
            : new TsCallExpression(new TsRuntimeHelperReference("isString"), [value]);

    /// <summary>
    /// An integral number in the underlying type's range: its validating
    /// check, or a safe integer for a 64-bit underlying type, whose values
    /// stay JS numbers.
    /// </summary>
    private static TsExpression IntegralTest(TsExpression value, IrNumericKind underlying) =>
        TsRuntimePrimitive.OfIntegral(underlying)?.Validator is { } validator
            ? new TsCallExpression(new TsRuntimeHelperReference(validator), [value])
            : new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Number"), "isSafeInteger"),
                [value]
            );
}
