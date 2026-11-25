using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The pieces a lowered <c>using</c> statement or declaration (FR-111) needs from the
/// expression extractor: a fresh temporary for an expression resource and the
/// <c>Dispose()</c> call on a resource, built from the method symbol like any other call
/// so the emitted name follows the ordinary member-name policy.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>A fresh name for the resource of a <c>using (expression)</c>.</summary>
    internal string NextUsingResourceName() => _temporaries.Next("$using");

    /// <summary>
    /// <c>resource.Dispose()</c>, or <c>resource?.Dispose()</c> when
    /// <paramref name="mayBeNull"/>: C# skips <c>Dispose</c> on a <c>null</c> resource.
    /// </summary>
    internal IrExpression DisposeCall(IrExpression resource, IMethodSymbol dispose, bool mayBeNull)
    {
        if (!mayBeNull)
            return InstanceCallWithoutArguments(resource, dispose);

        return new IrNullConditionalAccess(
            resource,
            InstanceCallWithoutArguments(new IrConditionalReceiver(), dispose)
        );
    }

    private IrCallExpression InstanceCallWithoutArguments(
        IrExpression receiver,
        IMethodSymbol method
    )
    {
        var origin = BuildOrigin(method);

        var overload = DispatchedMethodCalls.RefOf(method, _context) is { } reference
            ? MarkSynthesized(new IrSelectedOverload(reference, EvaluationOrder: null), method)
            : null;

        return new IrCallExpression(
            new IrMemberAccess(receiver, method.Name, origin) { Overload = overload },
            [],
            Origin: origin
        )
        {
            Overload = overload,
        };
    }
}
