using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Which <c>ArgumentOutOfRangeException.ThrowIf*</c> guards the runtime's
/// <c>argument-guards</c> functions lower for a given <c>T</c> (#651). .NET
/// constrains the sign guards to <c>INumberBase&lt;T&gt;</c>, the equality
/// guards to <c>IEquatable&lt;T&gt;</c> and the ordered ones to
/// <c>IComparable&lt;T&gt;</c>; the runtime reproduces those comparisons for
/// the values generated code holds of the kinds below, and nothing else. A
/// guard this policy does not admit keeps no mapping, so the call reports
/// <c>MS0001</c> as an unmapped static member: an ordered comparison of a
/// <c>string</c> is culture-sensitive on .NET, a <c>Guid</c> orders by its
/// bytes and a user type through its own <c>CompareTo</c>.
/// </summary>
internal static class IrToTsArgumentGuards
{
    private const string GuardsType = "System.ArgumentOutOfRangeException";

    private static readonly HashSet<string> SignGuards =
    [
        "ThrowIfZero",
        "ThrowIfNegative",
        "ThrowIfNegativeOrZero",
    ];

    private static readonly HashSet<string> OrderedGuards =
    [
        "ThrowIfGreaterThan",
        "ThrowIfGreaterThanOrEqual",
        "ThrowIfLessThan",
        "ThrowIfLessThanOrEqual",
    ];

    private static readonly HashSet<IrPrimitive> Numbers =
    [
        IrPrimitive.Byte,
        IrPrimitive.Int16,
        IrPrimitive.Int32,
        IrPrimitive.Int64,
        IrPrimitive.UInt64,
        IrPrimitive.Float32,
        IrPrimitive.Float64,
        IrPrimitive.Decimal,
        IrPrimitive.BigInteger,
        IrPrimitive.Char,
    ];

    private static readonly HashSet<IrPrimitive> OrderedPrimitives =
    [
        .. Numbers,
        IrPrimitive.Boolean,
        IrPrimitive.DateTime,
        IrPrimitive.DateTimeOffset,
        IrPrimitive.DateOnly,
        IrPrimitive.TimeOnly,
        IrPrimitive.TimeSpan,
    ];

    /// <summary>
    /// Whether the declarative mapping of <paramref name="call"/> may apply:
    /// every call but a sign or ordered guard over a <c>T</c> the runtime
    /// cannot compare as .NET does (the equality guards admit every <c>T</c>,
    /// through <c>EqualityComparer&lt;T&gt;.Default</c>'s rules).
    /// </summary>
    public static bool Admits(IrCallExpression call)
    {
        if (call.Origin is not { DeclaringTypeFullName: GuardsType, MemberName: var name })
            return true;

        var compared = call.TypeArguments is [var only] ? only : null;

        if (SignGuards.Contains(name))
            return compared is IrPrimitiveTypeRef { Primitive: var primitive }
                && Numbers.Contains(primitive);

        if (OrderedGuards.Contains(name))
            return IsOrdered(compared);

        return true;
    }

    private static bool IsOrdered(IrTypeRef? type) =>
        type is IrPrimitiveTypeRef { Primitive: var primitive }
        && OrderedPrimitives.Contains(primitive);
}
