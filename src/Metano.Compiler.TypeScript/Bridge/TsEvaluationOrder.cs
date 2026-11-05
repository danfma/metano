using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Keeps C#'s evaluation order for bound arguments passed in parameter order
/// (spec 013 FR-017, <see cref="IrSelectedOverload.EvaluationOrder"/>): when
/// named arguments put a side effect out of parameter order, every argument
/// is bound to a name in the order the source evaluates it, and the call
/// passes the names in parameter order. A spread argument binds its array and
/// passes it spread.
/// </summary>
internal static class TsEvaluationOrder
{
    /// <summary>
    /// <paramref name="arguments"/> (lowered, in parameter order) with the
    /// bindings that evaluate them in <paramref name="order"/>; no bindings
    /// when <paramref name="order"/> is <c>null</c>. Each binding is named
    /// <paramref name="prefix"/> plus its parameter position.
    /// </summary>
    public static TsOrderedArguments Bind(
        IReadOnlyList<TsExpression> arguments,
        IReadOnlyList<int>? order,
        string prefix
    )
    {
        if (order is null)
            return new TsOrderedArguments([], arguments);

        // A trailing null `params` array was dropped from the lowered
        // arguments (a rest parameter cannot take it), so its position binds
        // nothing.
        var bindings = order
            .Where(position => position < arguments.Count)
            .Select(position => new TsImmediateBinding(
                prefix + position,
                Unspread(arguments[position])
            ))
            .ToList();

        var passed = arguments
            .Select((argument, position) => Respread(argument, new TsIdentifier(prefix + position)))
            .ToList();

        return new TsOrderedArguments(bindings, passed);
    }

    private static TsExpression Unspread(TsExpression argument) =>
        argument is TsSpreadExpression spread ? spread.Expression : argument;

    private static TsExpression Respread(TsExpression argument, TsIdentifier name) =>
        argument is TsSpreadExpression ? new TsSpreadExpression(name) : name;
}

/// <summary>
/// Arguments to pass in parameter order and the bindings that evaluate them
/// first, in the source's order.
/// </summary>
internal sealed record TsOrderedArguments(
    IReadOnlyList<TsImmediateBinding> Bindings,
    IReadOnlyList<TsExpression> Arguments
);
