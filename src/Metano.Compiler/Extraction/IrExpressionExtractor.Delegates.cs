using Metano.Compiler.Extraction.Invocation;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Delegate shapes the generic method-group and lambda paths get wrong: a
/// delegate's own <c>Invoke</c> used as a method group, and a lambda converted
/// to a delegate whose last parameter is <c>params</c>.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// A delegate's <c>Invoke</c> used as a method group (<c>Action&lt;H, int&gt;
    /// f = h.Prop.Invoke;</c>, #375) converts the delegate value itself, read
    /// once at the conversion as C# does: the value as it is when both
    /// delegates agree on <c>[This]</c>, or wrapped in <c>bindReceiver</c> when
    /// only the target delegate binds its first parameter to <c>this</c>. A
    /// <c>[This]</c> delegate converted to a plain one has no lowering yet and
    /// reports MS0001. <c>null</c> for any other method group.
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

        var sourceBindsThis = InvocationLoweringHelpers.HasThisParameter(method);
        var targetBindsThis = InvocationLoweringHelpers.HasThisParameter(targetInvoke);

        if (IsDartTarget || sourceBindsThis == targetBindsThis)
            return invoked;

        return targetBindsThis
            ? new IrCallExpression(new IrIdentifier("bindReceiver"), [new IrArgument(invoked)])
            : ReportUnsupported(
                expression,
                "DelegateInvokeGroup",
                $"'{expression}' converts a delegate whose first parameter is [This] to one whose first parameter is not; it is not supported by the transpiler."
            );
    }

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
}
