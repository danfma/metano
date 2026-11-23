using System.Diagnostics.CodeAnalysis;
using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// <c>DefaultIfEmpty</c> (#617): a lazy stage that yields <c>default(TSource)</c>, or
/// the <c>defaultValue</c> argument, when the source is empty. Its runtime operator takes
/// the value alone, and <c>null</c> is the value for reference and
/// <c>Nullable&lt;T&gt;</c> elements, which the stage then omits.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    // TODO(#549): the Dart target lowers neither these stages nor the LINQ chains they
    // belong to: it keeps the plain member calls of its own collections.
    private bool LowersLinqStage(IrLinqOperator op) =>
        _target is not TargetLanguage.Dart || !IrLinqMapping.IsTypeScriptRuntimeStage(op);

    /// <summary>
    /// Whether <paramref name="symbol"/> is a LINQ method the pipe form lowers for the
    /// current target.
    /// </summary>
    private bool TryResolveLinq([NotNullWhen(true)] IMethodSymbol? symbol, out IrLinqOperator op) =>
        IrLinqMapping.TryResolve(symbol, out op) && LowersLinqStage(op);

    /// <summary>
    /// The arguments of a <c>DefaultIfEmpty</c> stage: none when the miss value is
    /// <c>null</c>, otherwise the value alone (<see cref="OrDefaultArguments"/> without
    /// the predicate slot the <c>OrDefault</c> terminals lead with).
    /// </summary>
    private LinqStageArguments DefaultIfEmptyArguments(
        IMethodSymbol method,
        IReadOnlyList<IrArgument> bound,
        IReadOnlyList<int>? sourceOrder
    )
    {
        var arguments = OrDefaultArguments(method, bound, sourceOrder);

        return arguments.Values.Count == 2
            ? arguments with
            {
                Values = [arguments.Values[1]],
            }
            : arguments;
    }
}
