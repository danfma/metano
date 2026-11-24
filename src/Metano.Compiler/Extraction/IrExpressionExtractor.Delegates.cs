using Metano.Compiler.Extraction.Invocation;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Delegate shapes the generic method-group and lambda paths get wrong: a
/// delegate's own <c>Invoke</c> used as a method group, and a lambda converted
/// to a delegate whose last parameter is <c>params</c>, and a direct call of a
/// <c>[This]</c> delegate.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// A delegate's <c>Invoke</c> used as a method group (<c>Action&lt;H, int&gt;
    /// f = h.Prop.Invoke;</c>, #375) converts the delegate value itself, read
    /// once at the conversion as C# does: the value as it is when both
    /// delegates agree on <c>[This]</c>, or wrapped in <c>bindReceiver</c> when
    /// only the target delegate binds its first parameter to <c>this</c>. A
    /// <c>[This]</c> delegate converted to a plain one, and a conversion where
    /// only one delegate ends in a <c>params</c> parameter (whose calls spread
    /// their elements), have no lowering yet and report MS0001. Dart keeps the
    /// value as it is. <c>null</c> for any other method group.
    /// </summary>
    private IrExpression? DelegateInvokeGroupValue(
        ExpressionSyntax expression,
        IMethodSymbol method,
        IMethodSymbol targetInvoke,
        IrExpression reference
    )
    {
        if (
            method.MethodKind is not MethodKind.DelegateInvoke
            || reference is not IrMemberAccess { Target: var invoked }
            || invoked is IrTypeReference
        )
            return null;

        if (IsDartTarget)
            return invoked;

        if (EndsInParams(method) != EndsInParams(targetInvoke))
            return ReportUnsupported(
                expression,
                "DelegateInvokeGroup",
                $"'{expression}' converts a delegate whose last parameter is params to one whose last parameter is not, or the reverse; it is not supported by the transpiler."
            );

        var sourceBindsThis = InvocationLoweringHelpers.HasThisParameter(method);
        var targetBindsThis = InvocationLoweringHelpers.HasThisParameter(targetInvoke);

        if (sourceBindsThis == targetBindsThis)
            return invoked;

        return targetBindsThis
            ? new IrCallExpression(new IrIdentifier("bindReceiver"), [new IrArgument(invoked)])
            : ReportUnsupported(
                expression,
                "DelegateInvokeGroup",
                $"'{expression}' converts a delegate whose first parameter is [This] to one whose first parameter is not; it is not supported by the transpiler."
            );
    }

    // A call through a `params` delegate passes its elements one by one, so a
    // delegate value is only interchangeable with one of the same shape.
    private static bool EndsInParams(IMethodSymbol invoke) =>
        invoke.Parameters is [.., { IsParams: true }];

    /// <summary>
    /// <paramref name="parameters"/> with the last one marked <c>params</c>
    /// when the delegate the lambda converts to declares its last parameter
    /// <c>params</c> (#338): a call through the delegate passes the elements
    /// one by one, so the lambda receives them as a rest parameter.
    /// </summary>
    private IReadOnlyList<IrParameter> WithTargetParams(
        LambdaExpressionSyntax lambda,
        IReadOnlyList<IrParameter> parameters
    ) =>
        ConvertedDelegateInvoke(lambda) is { Parameters: [.., { IsParams: true }] } invoke
        && invoke.Parameters.Length == parameters.Count
            ? [.. parameters.SkipLast(1), parameters[^1] with { IsParams = true }]
            : parameters;

    private IMethodSymbol? ConvertedDelegateInvoke(ExpressionSyntax expression) =>
        _semantic.GetTypeInfo(expression).ConvertedType
            is INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke }
            ? invoke
            : null;

    /// <summary>
    /// A direct call of a <c>[This]</c> delegate (<c>listener(button, "click")</c>,
    /// <c>handler.Invoke(h, y: 1, x: 2)</c>) lowered to <c>.call</c> on the
    /// delegate, so the JS dispatch sets <c>this</c> to the receiver before the
    /// runtime trampoline forwards it back. The arguments are put in parameter
    /// order first, so the receiver is the first one however the source names
    /// them (#376), and the call keeps C#'s evaluation order.
    /// </summary>
    private IrCallExpression ThisDelegateCall(
        InvocationExpressionSyntax inv,
        IMethodSymbol invoke,
        IrExpression target,
        IReadOnlyList<IrArgument> args
    )
    {
        var ordered = InParameterOrder(args, invoke);

        return new IrCallExpression(
            new IrMemberAccess(target, "call"),
            [ordered[0] with { Name = null }, .. ordered.Skip(1)]
        )
        {
            EvaluationOrder = ReportedSourceOrderOf(_semantic.GetOperation(inv), ordered),
        };
    }
}
