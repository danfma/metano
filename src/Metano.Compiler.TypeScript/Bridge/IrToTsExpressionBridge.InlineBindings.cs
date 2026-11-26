using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Values a lowering evaluates once, ahead of the expression that reads them
/// (arguments C# evaluates out of parameter order, a receiver evaluated first),
/// bound with no closure (W13-1, #804): each binding assigns a temporary of the
/// enclosing statement list inline, <c>($arg1 = b(), $arg0 = a(), f($arg0, $arg1))</c>.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>
    /// <paramref name="result"/> behind the <paramref name="bindings"/> that
    /// evaluate its parts first and the <paramref name="effects"/> that run after
    /// them; <paramref name="result"/> itself when there are neither. With no
    /// binding the effects need no temporary: <c>(effect, result)</c>.
    /// </summary>
    private static TsExpression BoundBefore(
        IReadOnlyList<TsImmediateBinding> bindings,
        TsExpression result,
        IrToTsBridgeContext context,
        IReadOnlyList<TsExpression>? effects = null
    )
    {
        effects ??= [];

        if (bindings.Count == 0)
            return effects.Count == 0 ? result : Sequence([.. effects, result]);

        if (BindsInline(bindings, [.. effects, result], context))
        {
            foreach (var binding in bindings)
                context.Temporaries!.DeclareClaimed(new TsIdentifier(binding.Name));

            return Sequence([
                .. bindings.Select(binding => new TsBinaryExpression(
                    new TsIdentifier(binding.Name),
                    "=",
                    binding.Value
                )),
                .. effects,
                result,
            ]);
        }

        // Outside any statement list (a static initializer, a field default), or
        // where the expression may create a closure that reads a binding, which
        // then needs a scope of its own per evaluation: the bindings are the
        // parameters of an arrow invoked on the spot.
        return TsImmediateInvocation.Of(
            bindings,
            [
                .. effects.Select(effect => new TsExpressionStatement(effect)),
                new TsReturnStatement(result),
            ],
            isAsync: false
        );
    }

    /// <summary>
    /// Whether the bindings can assign temporaries of the statement list: each
    /// name is one the list claimed (<see cref="TsEvaluationOrder.NameOf"/>), and
    /// no function created in <paramref name="readers"/> reads one
    /// (<see cref="TsClosureReads"/>).
    /// </summary>
    private static bool BindsInline(
        IReadOnlyList<TsImmediateBinding> bindings,
        IReadOnlyList<TsExpression> readers,
        IrToTsBridgeContext context
    )
    {
        if (
            context.Temporaries is not { } temporaries
            || !bindings.All(binding => temporaries.HoldsClaim(binding.Name))
        )
            return false;

        var names = bindings.Select(binding => binding.Name).ToHashSet(StringComparer.Ordinal);

        return !readers.Any(reader => TsClosureReads.AnyIn(reader, names));
    }

    /// <summary>The comma expression <c>(first, …, last)</c>.</summary>
    private static TsExpression Sequence(IReadOnlyList<TsExpression> parts) =>
        new TsParenthesized(
            parts.Aggregate((left, right) => new TsBinaryExpression(left, ",", right))
        );
}
