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
    /// after <paramref name="prefix"/> plus its parameter position: a name the
    /// statement list of <paramref name="context"/> claims, which no other
    /// binding there takes, so the bindings may be assigned inline; the bare
    /// stem outside a statement list.
    /// </summary>
    public static TsOrderedArguments Bind(
        IReadOnlyList<TsExpression> arguments,
        IReadOnlyList<int>? order,
        string prefix,
        IrToTsBridgeContext? context
    )
    {
        if (order is null)
            return new TsOrderedArguments([], arguments);

        var names = Enumerable
            .Range(0, arguments.Count)
            .Select(position => NameOf(prefix + position, context))
            .ToList();

        // A trailing null `params` array was dropped from the lowered
        // arguments (a rest parameter cannot take it), so its position binds
        // nothing.
        var bindings = order
            .Where(position => position < arguments.Count)
            .Select(position => new TsImmediateBinding(
                names[position].Name,
                Unspread(arguments[position])
            ))
            .ToList();

        var passed = arguments
            .Select((argument, position) => Respread(argument, names[position]))
            .ToList();

        return new TsOrderedArguments(bindings, passed);
    }

    /// <summary>
    /// The name of a binding after <paramref name="stem"/>: claimed from the
    /// statement list of <paramref name="context"/> (skipping the locals its
    /// enclosing loops reserve), or the stem itself outside a statement list.
    /// </summary>
    public static TsIdentifier NameOf(string stem, IrToTsBridgeContext? context) =>
        context?.Temporaries?.Claim(stem, context.ReservedLocals) ?? new TsIdentifier(stem);

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
