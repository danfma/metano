using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The LINQ calls the runtime operators do not implement (#648): a stage
/// whose overload its operator lacks, a comparer the operator would ignore,
/// a queryable tree the operator has no slot for, and an extension method of
/// <c>System.Linq.Enumerable</c> / <c>System.Linq.Queryable</c> that nothing
/// lowers. Each reports MS0001 and lowers to the throwing placeholder rather
/// than to another overload or to a member call on the receiver.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string UnsupportedLinqOverloadKind = "UnsupportedLinqOverload";

    private const string UnloweredLinqCallKind = "UnloweredLinqCall";

    /// <summary>
    /// Why the runtime operator of <paramref name="op"/> cannot run the
    /// overload <paramref name="call"/> binds to — it lacks the overload, or
    /// would ignore its comparer — or <c>null</c> when it can or the target
    /// does not lower LINQ chains.
    /// </summary>
    private UnsupportedLinqStage? UnimplementedStageOf(
        InvocationExpressionSyntax call,
        IMethodSymbol method,
        IrLinqOperator op
    )
    {
        if (!LowersLinqChains)
            return null;

        return IrLinqMapping.OverloadSupportOf(method, op) switch
        {
            IrLinqOverloadSupport.Unimplemented => new UnsupportedLinqStage(
                call,
                UnimplementedOverloadMessage(method, op)
            ),
            IrLinqOverloadSupport.IgnoredComparer => new UnsupportedLinqStage(
                ComparerArgumentOf(call, method),
                IgnoredComparerMessage(method)
            ),
            _ => null,
        };
    }

    /// <summary>
    /// The report for <paramref name="stage"/> when it captures a queryable
    /// tree its runtime operator does not read where the stage passes it
    /// (<c>queryable.ToDictionary(x =&gt; x)</c> would hand the tree to the
    /// value selector); <c>null</c> otherwise, or when the target does not
    /// lower LINQ chains.
    /// </summary>
    private UnsupportedLinqStage? MisplacedTreeStageOf(
        InvocationExpressionSyntax call,
        IMethodSymbol method,
        IrLinqStage stage
    ) =>
        LowersLinqChains
        && stage.Queryable is not null
        && !IrLinqMapping.ReadsQueryableTreeAfter(stage.Operator, stage.Arguments.Count)
            ? new UnsupportedLinqStage(call, MisplacedQueryableTreeMessage(method, stage.Operator))
            : null;

    /// <summary>
    /// A mapped LINQ call the chain walker left to the plain call — the
    /// static form (<c>Enumerable.Aggregate(xs, …)</c>, #619), or a stage of a
    /// chain with a static-form stage inside it — when its runtime operator
    /// cannot run the overload; <c>null</c> otherwise.
    /// </summary>
    private UnsupportedLinqStage? UnimplementedUnchainedLinqCallOf(
        InvocationExpressionSyntax call,
        IMethodSymbol? symbol
    ) =>
        symbol is not null && IrLinqMapping.TryResolve(symbol, out var op)
            ? UnimplementedStageOf(call, symbol, op)
            : null;

    /// <summary>
    /// Whether <paramref name="symbol"/> is a LINQ extension method called on
    /// its receiver that neither the pipe operators nor a declarative mapping
    /// of the target lower.
    /// </summary>
    private bool IsUnloweredLinqCall([NotNullWhen(true)] IMethodSymbol? symbol) =>
        LowersLinqChains
        && symbol is { ReducedFrom: not null }
        && IrLinqMapping.IsLinqMethod(symbol)
        && !IrLinqMapping.TryResolve(symbol, out _)
        && !LowersByTargetMapping(symbol);

    /// <summary>
    /// Whether a declarative mapping of the target lowers
    /// <paramref name="method"/>; <c>true</c> when the target declares no
    /// mappings port, so its calls are never reported.
    /// </summary>
    private bool LowersByTargetMapping(IMethodSymbol method) =>
        _context.LowersByDeclarativeMapping?.Invoke(method) ?? true;

    /// <summary>
    /// The argument <paramref name="call"/> passes as its comparer, or the
    /// call itself when no argument names it.
    /// </summary>
    private SyntaxNode ComparerArgumentOf(InvocationExpressionSyntax call, IMethodSymbol method)
    {
        var comparer = call.ArgumentList.Arguments.FirstOrDefault(argument =>
            BoundParameterOf(argument, method).Name == IrLinqMapping.ComparerParameterName
        );

        return comparer is null ? call : comparer.Expression;
    }

    /// <summary>
    /// Reports MS0001 for each stage of a chain the runtime operators cannot
    /// run, then returns the placeholder the whole chain lowers to. The walk
    /// collects the stages terminal first, so they are reported in source
    /// order.
    /// </summary>
    private static IrUnsupportedExpression ReportUnsupportedLinqStages(
        IReadOnlyList<UnsupportedLinqStage> stages
    )
    {
        foreach (var stage in stages.Reverse())
            ReportUnsupportedFeature(stage.Location, stage.Message);

        return new IrUnsupportedExpression(UnsupportedLinqOverloadKind);
    }

    private static IrUnsupportedExpression ReportUnloweredLinqCall(
        InvocationExpressionSyntax call,
        IMethodSymbol method
    ) =>
        ReportUnsupported(
            call,
            UnloweredLinqCallKind,
            $"The LINQ method '{method.Name}' has no runtime counterpart; "
                + "the call throws at run time."
        );

    /// <summary>
    /// Names the overload <paramref name="method"/> binds to and the
    /// overloads of its declaring type the runtime operator implements.
    /// </summary>
    private static string UnimplementedOverloadMessage(IMethodSymbol method, IrLinqOperator op)
    {
        var signature = IrLinqMapping.RuntimeSignatureOf(op);

        var implemented = method
            .ContainingType.GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Select(IrLinqMapping.ParameterNamesOf)
            .Where(signature.Implements)
            .Select(overload => $"'{OverloadName(method.Name, overload)}'")
            .Distinct();

        return $"The overload '{BoundOverloadName(method)}' is not supported; the runtime "
            + $"implements only {string.Join(" and ", implemented)}. The call throws at run time.";
    }

    private static string IgnoredComparerMessage(IMethodSymbol method) =>
        $"The comparer of '{method.Name}' is not supported; the default comparison would be "
        + "used. The call throws at run time.";

    private static string MisplacedQueryableTreeMessage(IMethodSymbol method, IrLinqOperator op) =>
        $"The expression tree of the queryable '{BoundOverloadName(method)}' has no slot in the "
        + $"runtime operator '{IrLinqMapping.EmittedName(op)}'. The call throws at run time.";

    private static string BoundOverloadName(IMethodSymbol method) =>
        OverloadName(method.Name, IrLinqMapping.ParameterNamesOf(method));

    private static string OverloadName(string methodName, IReadOnlyList<string> parameters) =>
        $"{methodName}({string.Join(", ", parameters)})";

    /// <summary>
    /// A LINQ stage the runtime operators cannot run: where to report it and
    /// what to say.
    /// </summary>
    private sealed record UnsupportedLinqStage(SyntaxNode Location, string Message);
}
