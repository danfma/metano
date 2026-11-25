using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The value a type test binds when the tested type is a closed type lowered to a
/// union (spec 010 FR-C5). <c>instanceof Seek</c> narrows a value declared outside the
/// family (<c>object</c>, a type parameter) to the runtime class <c>Seek$Base</c>,
/// which the union <c>Seek</c> does not accept, so the binding asserts it to the union.
/// The assertion goes through <c>unknown</c>, as the runtime class is not comparable
/// with a union whose cases override a protected member. A value declared as a closed
/// type already narrows to the union's members and binds unchanged.
/// </summary>
internal static class ClosedFamilyNarrowing
{
    /// <summary>
    /// <paramref name="value"/>, which passed a test against <paramref name="type"/>,
    /// typed as <paramref name="type"/> when <paramref name="inputType"/> (the value's
    /// declared type) leaves it narrowed to the runtime class.
    /// </summary>
    public static TsExpression Bind(
        TsExpression value,
        IrTypeRef? type,
        IrTypeRef? inputType,
        IrToTsBridgeContext context
    ) =>
        NeedsAssertion(type, inputType, context)
            ? new TsCastExpression(
                new TsCastExpression(value, new TsNamedType("unknown")),
                IrToTsTypeMapper.Map(type!)
            )
            : value;

    /// <summary>
    /// The tested type is a closed type of a family lowered to the union (a suppressed
    /// family keeps its class, which narrowing already reaches), and the input is not
    /// itself typed as a closed type.
    /// </summary>
    private static bool NeedsAssertion(
        IrTypeRef? type,
        IrTypeRef? inputType,
        IrToTsBridgeContext context
    ) =>
        type is IrNamedTypeRef { Semantics.IsClosed: true } closed
        && context.IsUnionFamily(closed)
        && !IsClosed(StripNullable(inputType));

    private static bool IsClosed(IrTypeRef? type) =>
        type is IrNamedTypeRef { Semantics.IsClosed: true };

    private static IrTypeRef? StripNullable(IrTypeRef? type) =>
        type is IrNullableTypeRef nullable ? nullable.Inner : type;
}
