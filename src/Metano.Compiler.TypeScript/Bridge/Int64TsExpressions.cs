using System.Globalization;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript expressions the 64-bit lowering of spec 012 is built from:
/// the <c>BigInt</c> / <c>Number</c> globals, the native
/// <c>BigInt.asIntN</c> / <c>asUintN</c> wraps and the calls of the
/// <c>metano-runtime</c> helpers, shared by the conversion and overflow
/// bridges.
/// </summary>
internal static class Int64TsExpressions
{
    public static TsExpression AsIntN(bool isSigned, int bits, TsExpression value) =>
        Global(
            isSigned ? "BigInt.asIntN" : "BigInt.asUintN",
            new TsLiteral(bits.ToString(CultureInfo.InvariantCulture)),
            value
        );

    public static TsExpression BigInt(TsExpression value) => Global("BigInt", value);

    public static TsExpression Number(TsExpression value) => Global("Number", value);

    /// <summary>A call of a <c>metano-runtime</c> helper, imported through its reference node.</summary>
    public static TsCallExpression RuntimeCall(string name, params TsExpression[] arguments) =>
        new(new TsRuntimeHelperReference(name), arguments);

    /// <summary>
    /// A call of a JavaScript global; a dotted name (<c>BigInt.asIntN</c>)
    /// becomes a property access.
    /// </summary>
    public static TsCallExpression Global(string function, params TsExpression[] arguments)
    {
        var dot = function.IndexOf('.');

        TsExpression callee =
            dot < 0
                ? new TsIdentifier(function)
                : new TsPropertyAccess(new TsIdentifier(function[..dot]), function[(dot + 1)..]);

        return new TsCallExpression(callee, arguments);
    }
}
