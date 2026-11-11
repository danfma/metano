using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The LINQ calls the runtime operators do not implement (#648): a stage
/// whose overload its operator lacks, a comparer the operator would ignore,
/// a queryable tree the operator would read as another argument, and a
/// method of <c>System.Linq.Enumerable</c> / <c>System.Linq.Queryable</c>
/// that nothing lowers, in the extension or the static form. Each reports
/// MS0001 and lowers to the throwing placeholder rather than to another
/// overload or to a member call on the receiver. A <c>null</c> comparer and a
/// tree an operator reads nowhere are dropped instead: the call then means
/// what it means in C#.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string UnsupportedLinqOverloadKind = "UnsupportedLinqOverload";

    private const string UnloweredLinqCallKind = "UnloweredLinqCall";

    /// <summary>
    /// The last invocation rewriter
    /// (<see cref="Invocation.UnsupportedLinqCallRewriter"/>): a LINQ call no
    /// chain lowered — an unlowered method, or a mapped one in the static
    /// form or over a static-form stage whose runtime operator cannot run the
    /// overload — reports and becomes the placeholder; <c>null</c> for any
    /// other call.
    /// </summary>
    internal IrExpression? RewriteUnsupportedLinqCall(
        InvocationExpressionSyntax call,
        IMethodSymbol? symbol
    )
    {
        if (IsUnloweredLinqCall(call, symbol))
            return ReportUnloweredLinqCall(call, symbol);

        if (UnchainedStageOf(call, symbol) is { } unsupported)
            return ReportUnsupportedLinqStages([unsupported]);

        return null;
    }

    /// <summary>
    /// Why the runtime operator cannot run the overload <paramref name="site"/>
    /// binds to — it lacks the overload, or would ignore a comparer that is
    /// not <c>null</c> — or <c>null</c> when it can or the target does not
    /// lower LINQ chains.
    /// </summary>
    private UnsupportedLinqStage? UnsupportedStageOf(LinqStageSite site) =>
        UnsupportedStageOf(site.Call, site.Method, site.Op);

    private UnsupportedLinqStage? UnsupportedStageOf(
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
                MethodNameLocationOf(call),
                UnimplementedOverloadMessage(method, op)
            ),
            IrLinqOverloadSupport.IgnoredComparer => IgnoredComparerStageOf(call, method),
            _ => null,
        };
    }

    /// <summary>
    /// The report for a comparer the runtime operator would ignore, or
    /// <c>null</c> when the comparer is the <c>null</c> constant, which the
    /// comparer-less overload means (<see cref="NullComparerArgumentOf"/>).
    /// </summary>
    private UnsupportedLinqStage? IgnoredComparerStageOf(
        InvocationExpressionSyntax call,
        IMethodSymbol method
    )
    {
        var comparer = ComparerArgumentOf(call, method);

        if (comparer is not null && IsNullConstant(comparer))
            return null;

        return new UnsupportedLinqStage(
            comparer?.GetLocation() ?? MethodNameLocationOf(call),
            IgnoredComparerMessage(method)
        );
    }

    /// <summary>
    /// The argument <paramref name="site"/> passes as a comparer the runtime
    /// operator does not take when it is the <c>null</c> constant, so the
    /// stage lowers as the comparer-less overload without extracting it;
    /// <c>null</c> otherwise.
    /// </summary>
    private ArgumentSyntax? NullComparerArgumentOf(LinqStageSite site) =>
        LowersLinqChains
        && IrLinqMapping.OverloadSupportOf(site.Method, site.Op)
            is IrLinqOverloadSupport.IgnoredComparer
        && ComparerArgumentOf(site.Call, site.Method) is { Parent: ArgumentSyntax comparer } value
        && IsNullConstant(value)
            ? comparer
            : null;

    /// <summary>
    /// The report for <paramref name="stage"/> when it captures a queryable
    /// tree its runtime operator would read as another argument
    /// (<c>queryable.ToDictionary(x =&gt; x)</c> would hand the tree to the
    /// value selector); <c>null</c> otherwise, or when the target does not
    /// lower LINQ chains.
    /// </summary>
    private UnsupportedLinqStage? MisplacedTreeStageOf(LinqStageSite site, IrLinqStage stage) =>
        LowersLinqChains
        && stage.Queryable is not null
        && IrLinqMapping
            .RuntimeSignatureOf(stage.Operator)
            .MisplacesTreeAfter(stage.Arguments.Count)
            ? new UnsupportedLinqStage(
                MethodNameLocationOf(site.Call),
                MisplacedQueryableTreeMessage(site.Method, stage.Operator)
            )
            : null;

    /// <summary>
    /// A mapped LINQ call the chain walker left to the plain call — the
    /// static form (<c>Enumerable.Aggregate(xs, …)</c>, #619), or a stage
    /// over a static-form stage — when its runtime operator cannot run the
    /// overload; <c>null</c> otherwise.
    /// </summary>
    private UnsupportedLinqStage? UnchainedStageOf(
        InvocationExpressionSyntax call,
        IMethodSymbol? symbol
    ) => IrLinqMapping.TryResolve(symbol, out var op) ? UnsupportedStageOf(call, symbol, op) : null;

    /// <summary>
    /// Whether <paramref name="symbol"/> is a LINQ method, called in the
    /// extension or the static form, that neither the pipe operators nor a
    /// declarative mapping of the target lower.
    /// </summary>
    private bool IsUnloweredLinqCall(
        InvocationExpressionSyntax call,
        [NotNullWhen(true)] IMethodSymbol? symbol
    ) =>
        LowersLinqChains
        && IrLinqMapping.IsLinqMethod(symbol)
        && !IrLinqMapping.TryResolve(symbol, out _)
        && !LowersByTargetMapping(symbol, EmittedArgumentCount(call, symbol));

    /// <summary>
    /// Whether a declarative mapping of the target lowers the overload
    /// <paramref name="method"/> binds to when the call passes
    /// <paramref name="argumentCount"/> arguments; <c>true</c> when the
    /// target declares no mappings port, so its calls are never reported.
    /// </summary>
    private bool LowersByTargetMapping(IMethodSymbol method, int argumentCount) =>
        _context.LowersByDeclarativeMapping?.Invoke(method, argumentCount) ?? true;

    /// <summary>
    /// The number of arguments the plain call lowering passes for
    /// <paramref name="call"/>, which the target's BCL mapper counts: the
    /// written ones, or, when one is named, one per parameter of
    /// <paramref name="method"/> with the omitted ones filled by their
    /// defaults (<see cref="InParameterOrder"/>).
    /// </summary>
    private int EmittedArgumentCount(InvocationExpressionSyntax call, IMethodSymbol method)
    {
        IReadOnlyList<IrArgument> written =
        [
            .. call.ArgumentList.Arguments.Select(argument => new IrArgument(
                new IrLiteral(null, IrLiteralKind.Null),
                argument.NameColon?.Name.Identifier.ValueText
            )),
        ];

        return InParameterOrder(written, method).Count;
    }

    /// <summary>
    /// <paramref name="arguments"/> without the comparer of
    /// <paramref name="method"/>, the last parameter of every LINQ overload
    /// that takes one, and with its position dropped from the evaluation
    /// order.
    /// </summary>
    private static LinqStageArguments WithoutComparer(
        LinqStageArguments arguments,
        IMethodSymbol method
    )
    {
        var comparer = method
            .Parameters.Select(parameter => parameter.Name)
            .ToList()
            .IndexOf(ComparerParameterName);

        var order = arguments
            .EvaluationOrder?.Where(position => position != comparer)
            .Select(position => position > comparer ? position - 1 : position)
            .ToList();

        return arguments with
        {
            Values = [.. arguments.Values.Where((_, position) => position != comparer)],
            EvaluationOrder = order,
        };
    }

    /// <summary>
    /// The <c>null</c> literal in place of <paramref name="argument"/>, under
    /// its name, without extracting it.
    /// </summary>
    private static IrArgument SkippedArgument(ArgumentSyntax argument) =>
        new(new IrLiteral(null, IrLiteralKind.Null), argument.NameColon?.Name.Identifier.ValueText);

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
    )
    {
        ReportUnsupportedFeature(
            MethodNameLocationOf(call),
            $"The LINQ method '{method.Name}' has no runtime counterpart; "
                + "the call throws at run time."
        );

        return new IrUnsupportedExpression(UnloweredLinqCallKind);
    }

    private static void ReportUnsupportedFeature(Location location, string message) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                message,
                location
            )
        );

    /// <summary>
    /// The method name token of <paramref name="call"/>
    /// (<c>GroupBy</c> in <c>xs.Where(p).GroupBy(k, e)</c>), so two stages of
    /// one chain are reported apart; the whole call when it names no method.
    /// </summary>
    private static Location MethodNameLocationOf(InvocationExpressionSyntax call) =>
        call.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.GetLocation(),
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.GetLocation(),
            SimpleNameSyntax name => name.Identifier.GetLocation(),
            _ => call.GetLocation(),
        };

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
            .Select(LinqOverloadParameters.From)
            .Where(signature.Implements)
            .Distinct()
            .Select(overload => $"'{overload.ToString(method.Name)}'");

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
        LinqOverloadParameters.From(method).ToString(method.Name);

    /// <summary>
    /// One LINQ call of a chain: its syntax, the method it binds to, the
    /// operator that method resolves to and the member access naming it.
    /// </summary>
    private readonly record struct LinqStageSite(
        InvocationExpressionSyntax Call,
        IMethodSymbol Method,
        IrLinqOperator Op,
        QualifiedMemberSyntax MemberAccess
    );

    /// <summary>
    /// A LINQ stage the runtime operators cannot run: where to report it and
    /// what to say.
    /// </summary>
    private sealed record UnsupportedLinqStage(Location Location, string Message);
}
