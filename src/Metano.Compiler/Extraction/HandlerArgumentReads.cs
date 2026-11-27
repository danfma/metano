using System.Collections.Immutable;
using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The values a custom interpolated-string handler's constructor receives
/// through <c>[InterpolatedStringHandlerArgument]</c> (FR-112 item 4). C#
/// evaluates the call's receiver and arguments once, before the handler; the
/// lowering reads each referenced one again at the handler, which is sound only
/// for a repeatable read (<see cref="IrEvaluationTraits.IsRepeatableRead"/>)
/// with nothing but repeatable reads evaluated between it and the handler.
/// </summary>
internal static class HandlerArgumentReads
{
    /// <summary>
    /// The value each placeholder argument of <paramref name="construction"/>
    /// passes, keyed by the argument's value operation, or the reason one
    /// cannot be read again. <paramref name="extract"/> lowers a receiver or an
    /// argument the call also lowers, so its diagnostics are the call's.
    /// </summary>
    public static HandlerArgumentValues Of(
        IInterpolatedStringHandlerCreationOperation creation,
        IObjectCreationOperation construction,
        Func<IOperation, IrExpression> extract
    )
    {
        var placeholders = construction
            .Arguments.Where(argument => PlaceholderOf(argument) is not null)
            .ToList();

        if (placeholders.Count == 0)
            return HandlerArgumentValues.None;

        if (
            creation.Parent is not IArgumentOperation { Parent: { } call } handlerArgument
            || IrExpressionExtractor.WrittenArgumentsOf(call) is not { } callArguments
        )
            return HandlerArgumentValues.Unsupported("the handler is not a call argument");

        var values = new Dictionary<IOperation, IrExpression>();

        foreach (var argument in placeholders)
        {
            var (value, problem) = ReadOf(argument, call, callArguments, extract);

            if (value is null)
                return HandlerArgumentValues.Unsupported(problem!);

            values[argument.Value] = value;
        }

        var handlerIndex = callArguments.IndexOf(handlerArgument);
        var firstReadIndex = placeholders.Min(argument => ReadIndexOf(PlaceholderOf(argument)!));

        var changing = callArguments
            .Skip(firstReadIndex)
            .Take(handlerIndex - firstReadIndex)
            .FirstOrDefault(between =>
                between.Parameter?.RefKind is not RefKind.Out
                && !IrEvaluationTraits.IsRepeatableRead(extract(between.Value))
            );

        return changing is null
            ? new HandlerArgumentValues(values, Problem: null)
            : HandlerArgumentValues.Unsupported(
                $"the argument '{changing.Syntax}' is evaluated between a handler argument and "
                    + "the handler and may change it; bind it to a local first"
            );
    }

    /// <summary>
    /// The lowered receiver or argument behind <paramref name="argument"/>, or
    /// the reason it cannot be read again.
    /// </summary>
    private static (IrExpression? Value, string? Problem) ReadOf(
        IArgumentOperation argument,
        IOperation call,
        ImmutableArray<IArgumentOperation> callArguments,
        Func<IOperation, IrExpression> extract
    )
    {
        if (!IsPassedAsIs(argument.Value))
        {
            return (
                null,
                $"the handler argument '{argument.Value.Syntax}' is converted to the "
                    + $"constructor's parameter type '{argument.Parameter?.Type.ToDisplayString()}'; "
                    + "pass a value of that type"
            );
        }

        var referenced = ReferencedOperationOf(PlaceholderOf(argument)!, call, callArguments);

        if (ConditionalReceiverOf(referenced) is { } receiver)
            return (null, ConditionalReceiverProblem(receiver, referenced!, call));

        if (referenced is null)
            return (null, "the handler argument names nothing the call passes");

        var read = extract(referenced);

        if (!IrEvaluationTraits.IsRepeatableRead(read))
        {
            return (
                null,
                $"the handler argument '{referenced.Syntax}' is not a repeatable read (a "
                    + "local, a parameter, 'this', a constant, a field or plain auto-property of "
                    + "those, or an operator over them); bind it to a local first"
            );
        }

        return (read, null);
    }

    /// <summary>
    /// The null-conditional receiver <paramref name="referenced"/> reads, alone
    /// or at the root of a member chain (<c>a?.B</c>), or <c>null</c>.
    /// </summary>
    private static IConditionalAccessInstanceOperation? ConditionalReceiverOf(
        IOperation? referenced
    ) =>
        referenced
            ?.DescendantsAndSelf()
            .OfType<IConditionalAccessInstanceOperation>()
            .FirstOrDefault();

    /// <summary>
    /// The message for a receiver read through <paramref name="receiver"/>:
    /// <c>maybe</c> itself or a chain on it (<c>a?.B</c>), with the guarded
    /// call to write instead.
    /// </summary>
    private static string ConditionalReceiverProblem(
        IConditionalAccessInstanceOperation receiver,
        IOperation referenced,
        IOperation call
    )
    {
        var root = receiver.Syntax.ToString();
        var chain = ReferenceEquals(referenced, receiver) ? "" : referenced.Syntax.ToString();
        var shown = chain.Length == 0 ? root : $"{root}?{chain}";
        var method = (call as IInvocationOperation)?.TargetMethod.Name ?? "M";

        return $"the receiver '{shown}' is null-conditional, so it cannot be read again at the "
            + $"handler; write 'if ({root} != null) {root}{chain}.{method}(...)' instead";
    }

    private static IInterpolatedStringHandlerArgumentPlaceholderOperation? PlaceholderOf(
        IArgumentOperation argument
    ) =>
        argument.Value switch
        {
            IInterpolatedStringHandlerArgumentPlaceholderOperation placeholder => placeholder,
            IConversionOperation
            {
                Operand: IInterpolatedStringHandlerArgumentPlaceholderOperation placeholder,
            } => placeholder,
            _ => null,
        };

    /// <summary>
    /// Whether the constructor receives the placeholder's value unchanged: no
    /// conversion, or one that keeps the value (identity, reference).
    /// </summary>
    private static bool IsPassedAsIs(IOperation value) =>
        value is not IConversionOperation conversion
        || conversion.Conversion is { IsIdentity: true } or { IsReference: true };

    /// <summary>
    /// The call's receiver or argument a placeholder stands for, or <c>null</c>
    /// for the trailing validity placeholder and a receiver the call has not.
    /// Roslyn numbers <see cref="IInterpolatedStringHandlerArgumentPlaceholderOperation.ArgumentIndex"/>
    /// by written position, the order of the call's argument list.
    /// </summary>
    private static IOperation? ReferencedOperationOf(
        IInterpolatedStringHandlerArgumentPlaceholderOperation placeholder,
        IOperation call,
        ImmutableArray<IArgumentOperation> callArguments
    ) =>
        placeholder.PlaceholderKind switch
        {
            InterpolatedStringArgumentPlaceholderKind.CallsiteReceiver => (
                call as IInvocationOperation
            )?.Instance,
            InterpolatedStringArgumentPlaceholderKind.CallsiteArgument
                when placeholder.ArgumentIndex < callArguments.Length => callArguments[
                placeholder.ArgumentIndex
            ].Value,
            _ => null,
        };

    /// <summary>
    /// The position in the call's argument list after which the referenced
    /// value is read: the receiver runs before every argument.
    /// </summary>
    private static int ReadIndexOf(
        IInterpolatedStringHandlerArgumentPlaceholderOperation placeholder
    ) =>
        placeholder.PlaceholderKind is InterpolatedStringArgumentPlaceholderKind.CallsiteArgument
            ? placeholder.ArgumentIndex + 1
            : 0;
}

/// <summary>
/// The handler-argument values of one conversion, keyed by the constructor
/// argument's value operation, or the reason they cannot be read again.
/// </summary>
internal sealed record HandlerArgumentValues(
    IReadOnlyDictionary<IOperation, IrExpression> Values,
    string? Problem
)
{
    public static HandlerArgumentValues None { get; } =
        new(new Dictionary<IOperation, IrExpression>(), Problem: null);

    public static HandlerArgumentValues Unsupported(string problem) =>
        new(new Dictionary<IOperation, IrExpression>(), problem);
}
