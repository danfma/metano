using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The arguments a redirecting constructor passes to its target
/// (<c>: this.fromXY(v, v)</c>). The chain's arguments are recorded as written,
/// positional or named, but Dart's named constructors take positional
/// parameters only, so named ones are placed at their parameter's position and
/// a parameter they skip passes its default.
/// </summary>
internal static class DartConstructorChainArguments
{
    public static IReadOnlyList<IrArgument> Of(IrConstructorChain chain, DartIrRewriter rewriter) =>
        Positional(chain)
            .Select(argument => argument with { Value = rewriter.Rewrite(argument.Value)! })
            .ToList();

    private static IReadOnlyList<IrArgument> Positional(IrConstructorChain chain)
    {
        if (chain.Arguments.All(argument => argument.Name is null))
            return chain.Arguments;

        var parameters = chain.Target.PlainParameters;
        var positional = chain.Arguments.TakeWhile(argument => argument.Name is null).ToList();
        var named = chain
            .Arguments.Where(argument => argument.Name is not null)
            .ToDictionary(argument => argument.Name!, StringComparer.Ordinal);
        var lastPassed = LastPassedIndex(parameters, positional.Count, named);

        return parameters
            .Take(lastPassed + 1)
            .Select(
                (parameter, index) =>
                    index < positional.Count ? positional[index]
                    : named.GetValueOrDefault(parameter.Name) is { } argument
                        ? new IrArgument(argument.Value)
                    : new IrArgument(DefaultOf(parameter))
            )
            .ToList();
    }

    /// <summary>The last parameter an argument binds; the ones after it keep their default.</summary>
    private static int LastPassedIndex(
        IReadOnlyList<IrParameter> parameters,
        int positionalCount,
        IReadOnlyDictionary<string, IrArgument> named
    )
    {
        var last = positionalCount - 1;

        for (var index = 0; index < parameters.Count; index++)
        {
            if (named.ContainsKey(parameters[index].Name))
                last = Math.Max(last, index);
        }

        return last;
    }

    private static IrExpression DefaultOf(IrParameter parameter) =>
        parameter.DefaultValue
        ?? throw new InvalidOperationException(
            $"The chained constructor's parameter '{parameter.Name}' is skipped but has no default."
        );
}
