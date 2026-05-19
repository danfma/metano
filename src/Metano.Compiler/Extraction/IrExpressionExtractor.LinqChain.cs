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
    /// in those cases the legacy fluent emission path takes over.
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
    /// </summary>
    private IrLinqChain? BuildLinqChain(InvocationExpressionSyntax outermost)
    {
        var stages = new List<IrLinqStage>();
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

            if (current.Expression is not MemberAccessExpressionSyntax memberAccess)
                return null; // static-direct calls (e.g. Enumerable.Where(items, p)) — MVP only handles extension form.

            // Static call syntax (`Enumerable.Where(items, p)`) also matches
            // MemberAccess. Detect it by the receiver expression resolving
            // to a type symbol — extension form's receiver always resolves
            // to a value (parameter, local, member). Bail to legacy fluent
            // emission so the call shape stays correct.
            if (_semantic.GetSymbolInfo(memberAccess.Expression).Symbol is INamedTypeSymbol)
                return null;

            var args = current.ArgumentList.Arguments.Select(a => Extract(a.Expression)).ToList();
            var queryable = TryCaptureQueryableMeta(current, sym, memberAccess);
            stages.Add(new IrLinqStage(op, args, queryable));

            if (memberAccess.Expression is InvocationExpressionSyntax innerInv)
            {
                current = innerInv;
            }
            else
            {
                sourceSyntax = memberAccess.Expression;
                current = null;
            }
        }

        if (sourceSyntax is null)
            return null;

        stages.Reverse();
        var source = Extract(sourceSyntax);
        // Build-time fusion of adjacent stages (#207). Idempotent on
        // chains the rules don't match — returns the original instance.
        return IrLinqChainFuser.Fuse(new IrLinqChain(source, stages));
    }

    /// <summary>
    /// Detects whether <paramref name="stage"/> opted into expression-tree
    /// capture and, if so, walks the principal lambda's body into an
    /// <see cref="IrQueryableMeta"/>. Capture triggers are:
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
        MemberAccessExpressionSyntax memberAccess
    )
    {
        if (stageMethod is null)
            return null;

        var receiverIsQueryable = ReceiverIsIQueryable(memberAccess.Expression);
        var methodHasQueryable = HasQueryableAttribute(stageMethod);

        for (var i = 0; i < stage.ArgumentList.Arguments.Count; i++)
        {
            var argSyntax = stage.ArgumentList.Arguments[i];
            if (argSyntax.Expression is not LambdaExpressionSyntax lambda)
                continue;

            var paramSymbol = ResolveStageParameter(stageMethod, i);
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
                isExplicit
            );
            var meta = walker.TryExtract(lambda);
            if (meta is not null)
                return meta;
        }

        return null;
    }

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
    /// reordering at the call site is not honoured — matches the
    /// existing <see cref="TryCaptureQueryableMeta"/> convention.
    /// </para>
    /// <para>
    /// Object-creation calls (<c>new T(...)</c>) are not covered —
    /// they flow through <c>ExtractObjectCreation</c>, not
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
            inv.Expression is MemberAccessExpressionSyntax member
            && _semantic.GetSymbolInfo(member.Expression).Symbol is not INamedTypeSymbol
        )
            return member.Expression;
        if (inv.ArgumentList.Arguments.Count > 0)
            return inv.ArgumentList.Arguments[0].Expression;
        return null;
    }
}
