using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// LINQ chain detection + folding (pipe lowering) and the related
/// queryable trigger / opt-in machinery. Kept on the extractor partial
/// so each rewriter shares the recursive <c>Extract</c> entry point
/// and <c>_semantic</c> field without indirection.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    // ─── LINQ chain (pipe lowering) ────────────────────────────────────

    /// <summary>
    /// Attempts to fold the LINQ chain rooted at <paramref name="inv"/>
    /// into a single <see cref="IrLinqChain"/>. Returns null when the
    /// invocation is not a LINQ call, is not the outermost stage of its
    /// chain, or contains a method without a pipe-runtime counterpart —
    /// in those cases the legacy fluent emission path takes over. A chain
    /// with a stage its runtime operator does not implement is reported and
    /// lowers to the throwing placeholder (#648).
    /// </summary>
    private IrExpression? TryExtractLinqChain(InvocationExpressionSyntax inv, IMethodSymbol? symbol)
    {
        if (!IrLinqMapping.TryResolve(symbol, out _))
            return null;
        if (!IsOutermostLinqCall(inv))
            return null;
        return BuildLinqChain(inv);
    }

    /// <summary>
    /// True when this invocation is the outermost LINQ call of its
    /// chain — i.e. the parent context is not another LINQ stage.
    /// Detection: parent is not a member-access whose grandparent is
    /// itself a LINQ invocation.
    /// </summary>
    private bool IsOutermostLinqCall(InvocationExpressionSyntax inv)
    {
        if (inv.Parent is not MemberAccessExpressionSyntax member)
            return true;
        if (member.Parent is not InvocationExpressionSyntax outer)
            return true;
        var outerSymbol = _semantic.GetSymbolInfo(outer).Symbol as IMethodSymbol;
        return !IrLinqMapping.TryResolve(outerSymbol, out _);
    }

    /// <summary>
    /// Walks the chain inward from <paramref name="outermost"/> via raw
    /// syntax, collecting one <see cref="IrLinqStage"/> per LINQ call.
    /// The walk stops at the first non-LINQ receiver — that becomes the
    /// chain's <see cref="IrLinqChain.Source"/>. Stages are reversed so
    /// they appear in source-to-terminal order in the IR (matches the
    /// emission shape <c>linq(source, op1, op2, ..., opN)</c>).
    /// <para>
    /// A chain with a stage its runtime operator cannot run (#648) is decided
    /// before anything is extracted: each such stage is reported and the
    /// whole chain lowers to the throwing placeholder, so diagnostics inside
    /// its other stages and its source surface once the chain is supported.
    /// A queryable tree the operator has no slot for for that overload is
    /// known only once the stages are extracted; it replaces the chain the
    /// same way.
    /// </para>
    /// </summary>
    private IrExpression? BuildLinqChain(InvocationExpressionSyntax outermost)
    {
        if (WalkLinqChain(outermost) is not { } walk)
            return null;

        var (sites, sourceSyntax) = walk;

        var unsupportedStages = sites
            .Select(UnsupportedStageOf)
            .OfType<UnsupportedLinqStage>()
            .ToList();

        if (unsupportedStages.Count > 0)
            return ReportUnsupportedLinqStages(unsupportedStages);

        var stages = sites.Select(ExtractLinqStage).ToList();

        var misplacedTrees = sites
            .Zip(stages, MisplacedTreeStageOf)
            .OfType<UnsupportedLinqStage>()
            .ToList();

        if (misplacedTrees.Count > 0)
            return ReportUnsupportedLinqStages(misplacedTrees);

        stages.Reverse();
        var source = Extract(sourceSyntax);
        // Build-time fusion of adjacent stages (#207). Idempotent on
        // chains the rules don't match — returns the original instance.
        return IrLinqChainFuser.Fuse(new IrLinqChain(source, stages));
    }

    /// <summary>
    /// The stages of the chain rooted at <paramref name="outermost"/>,
    /// outermost first, and the syntax of its source; <c>null</c> when a stage
    /// is in the static form, which the legacy fluent emission path handles.
    /// </summary>
    private (IReadOnlyList<LinqStageSite> Sites, ExpressionSyntax Source)? WalkLinqChain(
        InvocationExpressionSyntax outermost
    )
    {
        var sites = new List<LinqStageSite>();
        InvocationExpressionSyntax? current = outermost;
        ExpressionSyntax? sourceSyntax = null;

        while (current is not null)
        {
            var sym = _semantic.GetSymbolInfo(current).Symbol as IMethodSymbol;
            if (!IrLinqMapping.TryResolve(sym, out var op))
            {
                // AsQueryable is BCL ceremony to lift an IEnumerable into
                // an IQueryable so the rest of the chain binds to
                // System.Linq.Queryable (and its Expression<Func<…>>
                // parameters). The runtime treats both as plain
                // Iterables, so we drill through it instead of leaving
                // the call as the chain source.
                if (IsAsQueryable(sym))
                {
                    var asqInner = ResolveAsQueryableInner(current);
                    if (asqInner is null)
                    {
                        sourceSyntax = current;
                        break;
                    }
                    if (asqInner is InvocationExpressionSyntax asqInv)
                    {
                        current = asqInv;
                        continue;
                    }
                    sourceSyntax = asqInner;
                    break;
                }

                // Inner call isn't a recognized LINQ method — its
                // result is the chain source (e.g. a domain helper
                // that returns IReadOnlyList<T> followed by a single
                // .Contains(x)). Stop walking; whatever stages were
                // already collected stay as the chain.
                sourceSyntax = current;
                break;
            }

            if (QualifiedMemberSyntax.From(current.Expression) is not { } memberAccess)
                return null; // static-direct calls (e.g. Enumerable.Where(items, p)) — MVP only handles extension form.

            // Static call syntax (`Enumerable.Where(items, p)`) also matches
            // MemberAccess. Detect it by the receiver expression resolving
            // to a type symbol — extension form's receiver always resolves
            // to a value (parameter, local, member). Bail to legacy fluent
            // emission so the call shape stays correct.
            if (_semantic.GetSymbolInfo(memberAccess.Receiver).Symbol is INamedTypeSymbol)
                return null;

            sites.Add(new LinqStageSite(current, sym, op, memberAccess));

            // A leading `?.Stage(…)` reads the null-conditional receiver,
            // which is already extracted (and bound): the chain starts there.
            if (
                memberAccess.Node is MemberAccessExpressionSyntax
                && memberAccess.Receiver is InvocationExpressionSyntax innerInv
            )
            {
                current = innerInv;
            }
            else
            {
                sourceSyntax = memberAccess.Receiver;
                current = null;
            }
        }

        return sourceSyntax is null ? null : (sites, sourceSyntax);
    }

    /// <summary>
    /// One stage of a chain, with its arguments bound to the parameters of
    /// its method and the queryable tree it captures. A <c>null</c> comparer
    /// is dropped, as the comparer-less overload means the same (#648), and
    /// so is a tree its runtime operator reads nowhere: the call then runs
    /// the plain operator, with the value C# computes.
    /// </summary>
    private IrLinqStage ExtractLinqStage(LinqStageSite site)
    {
        var (call, method, op, memberAccess) = site;
        var written = StageArguments(call, method, op);
        var args = OmitsNullComparer(site) ? WithoutComparer(written, method) : written;

        if (args.HasUnrepresentableMissValue && LowersLinqChains)
            ElementMissValue.ReportUnrepresentable(call, method, method.TypeArguments[0]);

        var queryable = TryCaptureQueryableMeta(call, method, memberAccess);
        var readTree = ReadsNoTree(op) ? null : queryable;

        return new IrLinqStage(op, args.Values, readTree, LinqSixtyFourBitValues.Of(method, op))
        {
            EvaluationOrder = args.EvaluationOrder,
        };
    }

    /// <summary>
    /// Whether the runtime operator of <paramref name="op"/> reads no
    /// queryable tree on a target that lowers LINQ chains.
    /// </summary>
    private bool ReadsNoTree(IrLinqOperator op) =>
        LowersLinqChains && IrLinqMapping.RuntimeSignatureOf(op).QueryableSlot is null;

    /// <summary>
    /// The arguments of a LINQ stage bound to the parameters of
    /// <paramref name="method"/> (#632): one per parameter the call passes,
    /// in parameter order, so a call that names them in another order hands
    /// each value to its own parameter. The order C# evaluates them in is
    /// kept beside them when it differs and can be observed
    /// (<see cref="SourceOrderOf"/>).
    /// </summary>
    private LinqStageArguments StageArguments(
        InvocationExpressionSyntax stage,
        IMethodSymbol method,
        IrLinqOperator op
    )
    {
        var written = stage.ArgumentList.Arguments.Select(ExtractArgument).ToList();
        var bound = InParameterOrder(written, method);
        var sourceOrder = SourceOrderOf(_semantic.GetOperation(stage), bound);

        return IrLinqMapping.IsOrDefaultTerminal(op)
            ? OrDefaultArguments(method, bound, sourceOrder)
            : new LinqStageArguments(ValuesOf(bound), EvaluationOrder: sourceOrder);
    }

    /// <summary>
    /// Detects whether <paramref name="stage"/> opted into expression-tree
    /// capture and, if so, walks the principal lambda's body into an
    /// <see cref="IrQueryableMeta"/>: the first capturable lambda in
    /// parameter order, whatever order named arguments were written in.
    /// Capture triggers are:
    /// <list type="bullet">
    ///   <item>The chain receiver implements <c>System.Linq.IQueryable&lt;T&gt;</c>.</item>
    ///   <item>The stage method or its argument parameter carries
    ///   <c>[Queryable]</c>.</item>
    ///   <item>The argument parameter type is <c>System.Linq.Expressions.Expression&lt;…&gt;</c>.</item>
    /// </list>
    /// Returns null when no trigger fires or the lambda body uses syntax
    /// outside the Phase B MVP subset; the caller drops the queryable
    /// surface and the lambda flows as a plain closure.
    /// </summary>
    private IrQueryableMeta? TryCaptureQueryableMeta(
        InvocationExpressionSyntax stage,
        IMethodSymbol? stageMethod,
        QualifiedMemberSyntax memberAccess
    )
    {
        if (stageMethod is null)
            return null;

        var receiverIsQueryable = ReceiverIsIQueryable(memberAccess.Receiver);
        var methodHasQueryable = HasQueryableAttribute(stageMethod);

        var argumentsInParameterOrder = stage
            .ArgumentList.Arguments.Select(argument =>
                (argument.Expression, ParameterIndex: ParameterIndexOf(argument, stageMethod))
            )
            .OrderBy(argument => argument.ParameterIndex);

        foreach (var (expression, parameterIndex) in argumentsInParameterOrder)
        {
            if (expression is not LambdaExpressionSyntax lambda)
                continue;

            var paramSymbol = ResolveStageParameter(stageMethod, parameterIndex);

            if (!ShouldCaptureExpressionTree(receiverIsQueryable, methodHasQueryable, paramSymbol))
                continue;

            // Explicit opt-in (#205): [Queryable] on method/param or
            // Expression<Func<…>> parameter type *without* IQueryable<T>
            // receiver in scope. IQueryable<T> auto-resolves stages to
            // System.Linq.Queryable (Expression<Func<…>> params), so
            // the receiver intent dominates — keep the silent bail
            // there.
            var trigger = new QueryableTriggerContext(
                receiverIsQueryable,
                methodHasQueryable,
                paramSymbol
            );

            var isExplicit = IsExplicitQueryableOptIn(trigger);

            var walker = new IrExpressionTreeExtractor(
                _semantic,
                _originResolver,
                _target,
                this,
                isExplicitOptIn: isExplicit
            );

            var meta = walker.TryExtract(lambda);

            if (meta is not null)
                return meta;
        }

        return null;
    }

    /// <summary>
    /// The position of the parameter <paramref name="argument"/> binds to,
    /// among the parameters the call site sees: an argument named out of
    /// position stands for its own parameter (#632).
    /// </summary>
    private int ParameterIndexOf(ArgumentSyntax argument, IMethodSymbol method) =>
        method.Parameters.IndexOf(
            BoundParameterOf(argument, method),
            0,
            SymbolEqualityComparer.Default
        );

    /// <summary>
    /// Runs the expression-tree walker on every lambda argument whose
    /// matching parameter explicitly opts into queryable capture
    /// (<c>[Queryable]</c> attribute or
    /// <c>Expression&lt;Func&lt;…&gt;&gt;</c> type) (#218). The walker
    /// is invoked purely for the MS0024 side-effect — the returned
    /// <see cref="IrQueryableMeta"/> is discarded because the
    /// surrounding IR has no slot for it on non-LINQ-chain
    /// invocations.
    /// <para>
    /// Mutually exclusive with the LINQ-chain path:
    /// <see cref="ExtractInvocation"/> returns early on
    /// <see cref="TryExtractLinqChain"/> hits, and inner LINQ stages
    /// never reach <see cref="ExtractInvocation"/> because
    /// <see cref="BuildLinqChain"/> consumes them syntactically. The
    /// receiver-is-IQueryable signal is intentionally excluded here —
    /// it remains an implicit opt-in handled by the chain path.
    /// </para>
    /// <para>
    /// Positional argument index is mapped to the parameter slot
    /// directly via <see cref="ResolveStageParameter"/>. Named-argument
    /// reordering at the call site is not honoured here, unlike
    /// <see cref="TryCaptureQueryableMeta"/>, which binds a chain stage's
    /// lambdas to the parameters they name (#648).
    /// </para>
    /// <para>
    /// Object-creation calls (<c>new T(...)</c>) are not covered —
    /// they flow through <c>ExtractCreation</c>, not
    /// <see cref="ExtractInvocation"/>. Constructor-arg
    /// <c>[Queryable]</c> opt-ins go unreported and are tracked as
    /// a #218 follow-up.
    /// </para>
    /// </summary>
    private void ReportQueryableDiagnosticsForExplicitOptIn(
        InvocationExpressionSyntax inv,
        IMethodSymbol? symbol
    )
    {
        if (symbol is null)
            return;

        for (var i = 0; i < inv.ArgumentList.Arguments.Count; i++)
        {
            var argSyntax = inv.ArgumentList.Arguments[i];

            if (argSyntax.Expression is not LambdaExpressionSyntax lambda)
                continue;

            var paramSymbol = ResolveStageParameter(symbol, i);

            if (!HasExplicitParameterOptIn(paramSymbol))
                continue;

            var walker = new IrExpressionTreeExtractor(
                _semantic,
                _originResolver,
                _target,
                this,
                isExplicitOptIn: true
            );

            walker.TryExtract(lambda);
        }
    }

    /// <summary>
    /// Maps a call-site argument index to the parameter symbol on the
    /// underlying static method. Extension calls reduce the receiver
    /// onto a synthesized first parameter, so the unreduced static
    /// definition's slot is one position to the right.
    /// </summary>
    private static IParameterSymbol? ResolveStageParameter(IMethodSymbol stageMethod, int argIndex)
    {
        var reduced = stageMethod.ReducedFrom ?? stageMethod;
        var isReducedExtension =
            stageMethod.IsExtensionMethod
            && !SymbolEqualityComparer.Default.Equals(reduced, stageMethod);
        var paramIndex = isReducedExtension ? argIndex + 1 : argIndex;
        if (paramIndex >= reduced.Parameters.Length)
            return null;
        return reduced.Parameters[paramIndex];
    }

    /// <summary>
    /// Snapshot of the three signals
    /// <see cref="ShouldCaptureExpressionTree"/> and
    /// <see cref="IsExplicitQueryableOptIn"/> consult: whether the
    /// receiver is <c>IQueryable&lt;T&gt;</c>, whether the stage
    /// method itself carries <c>[Queryable]</c>, and the resolved
    /// lambda parameter symbol (used to inspect its attribute set
    /// and declared type).
    /// </summary>
    private readonly record struct QueryableTriggerContext(
        bool ReceiverIsQueryable,
        bool MethodHasQueryable,
        IParameterSymbol? ParamSymbol
    );

    private static bool IsExplicitQueryableOptIn(QueryableTriggerContext trigger)
    {
        if (trigger.ReceiverIsQueryable)
            return false;
        if (trigger.MethodHasQueryable)
            return true;
        return HasExplicitParameterOptIn(trigger.ParamSymbol);
    }

    private static bool ShouldCaptureExpressionTree(
        bool receiverIsQueryable,
        bool methodHasQueryable,
        IParameterSymbol? paramSymbol
    )
    {
        if (receiverIsQueryable || methodHasQueryable)
            return true;
        return HasExplicitParameterOptIn(paramSymbol);
    }

    /// <summary>
    /// Parameter-level explicit queryable opt-in test: either the
    /// parameter itself carries <c>[Queryable]</c> or its declared
    /// type is <c>System.Linq.Expressions.Expression&lt;Func&lt;…&gt;&gt;</c>.
    /// Shared by the chain-aware <see cref="TryCaptureQueryableMeta"/>
    /// path and the broadened general-invocation path
    /// (<see cref="ReportQueryableDiagnosticsForExplicitOptIn"/>, #218).
    /// </summary>
    private static bool HasExplicitParameterOptIn(IParameterSymbol? paramSymbol) =>
        paramSymbol is not null
        && (HasQueryableAttribute(paramSymbol) || IsExpressionDelegateType(paramSymbol.Type));

    private static bool HasQueryableAttribute(ISymbol symbol) =>
        symbol
            .GetAttributes()
            .Any(attr =>
                attr.AttributeClass?.ToDisplayString() == "Metano.Annotations.QueryableAttribute"
            );

    private static bool IsExpressionDelegateType(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.ToDisplayString()
            == "System.Linq.Expressions.Expression<TDelegate>";

    private bool ReceiverIsIQueryable(ExpressionSyntax receiverSyntax)
    {
        var receiverType = _semantic.GetTypeInfo(receiverSyntax).Type;
        if (receiverType is null)
            return false;
        return IsIQueryableNamed(receiverType as INamedTypeSymbol)
            || receiverType.AllInterfaces.Any(IsIQueryableNamed);
    }

    private static bool IsIQueryableNamed(INamedTypeSymbol? type) =>
        type is not null
        && type.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>";

    /// <summary>
    /// True when <paramref name="symbol"/> is one of the
    /// <c>AsQueryable</c> overloads on <c>System.Linq.Queryable</c>.
    /// The BCL uses it purely to bind the next stage to
    /// <c>System.Linq.Queryable</c> (so lambda parameters become
    /// <c>Expression&lt;Func&lt;…&gt;&gt;</c>); the JS runtime has no
    /// such distinction, so the chain walker drops the call and keeps
    /// its receiver as the chain source.
    /// </summary>
    private static bool IsAsQueryable(IMethodSymbol? symbol) =>
        symbol is { Name: "AsQueryable" }
        && symbol.ContainingType?.ToDisplayString() == "System.Linq.Queryable";

    /// <summary>
    /// Returns the source expression behind an <c>AsQueryable</c> call,
    /// covering both syntactic forms:
    /// <list type="bullet">
    ///   <item>extension: <c>users.AsQueryable()</c> — receiver is the source;</item>
    ///   <item>static: <c>Queryable.AsQueryable(users)</c> — first argument is the source.</item>
    /// </list>
    /// Returns null for shapes outside the MVP (no member access, no
    /// argument), so the caller can fall back to treating the
    /// invocation itself as the chain source.
    /// </summary>
    private ExpressionSyntax? ResolveAsQueryableInner(InvocationExpressionSyntax inv)
    {
        if (
            QualifiedMemberSyntax.From(inv.Expression) is { } member
            && _semantic.GetSymbolInfo(member.Receiver).Symbol is not INamedTypeSymbol
        )
            return member.Receiver;
        if (inv.ArgumentList.Arguments.Count > 0)
            return inv.ArgumentList.Arguments[0].Expression;
        return null;
    }
}
