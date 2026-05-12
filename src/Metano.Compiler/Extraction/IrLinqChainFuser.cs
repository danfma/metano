using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Build-time fusion of adjacent LINQ stages on an <see cref="IrLinqChain"/>
/// (#207). Runs after <c>BuildLinqChain</c> and before the language bridge
/// consumes the chain, collapsing pairs the runtime would otherwise execute
/// as two pipe slots.
///
/// <para>Rules implemented for the MVP:</para>
/// <list type="bullet">
///   <item><c>Where(p).Where(q)</c> → single <c>Where(p &amp;&amp; q)</c>.
///   Queryable-meta predicates AND-combine their trees and union their
///   captures; closure predicates AND-combine via a synthesized lambda
///   that invokes both sub-lambdas. Mixed (one has meta, other does not)
///   stays unfused.</item>
///   <item><c>Take(a).Take(b)</c> → <c>Take(min(a, b))</c>, but only when
///   both arguments are compile-time numeric literals. A non-literal
///   would require a runtime <c>min</c> the runtime does not ship, so
///   the pair stays unfused in that case.</item>
///   <item><c>Skip(a).Skip(b)</c> → <c>Skip(a + b)</c> under the same
///   literal-only guard.</item>
///   <item><c>Reverse().Reverse()</c> → both stages dropped.</item>
/// </list>
///
/// <para>The pass uses a stack-shaped left-to-right walk so repeated
/// fusions ripple: <c>Reverse().Reverse().Reverse()</c> collapses to a
/// single <c>Reverse()</c> in one traversal.</para>
/// </summary>
public static class IrLinqChainFuser
{
    /// <summary>
    /// Returns a fused copy of <paramref name="chain"/>. When no rule
    /// matches the input is returned unchanged so callers can adopt the
    /// pass without per-shape gating.
    /// </summary>
    public static IrLinqChain Fuse(IrLinqChain chain)
    {
        if (chain.Stages.Count < 2)
            return chain;

        var fused = new List<IrLinqStage>(chain.Stages.Count);
        var changed = false;

        foreach (var next in chain.Stages)
        {
            if (fused.Count > 0 && TryFuse(fused[^1], next, out var combined))
            {
                fused.RemoveAt(fused.Count - 1);
                if (combined is not null)
                    fused.Add(combined);
                changed = true;
                continue;
            }
            fused.Add(next);
        }

        return changed ? new IrLinqChain(chain.Source, fused) : chain;
    }

    /// <summary>
    /// Attempts to fuse <paramref name="left"/> and <paramref name="right"/>
    /// into a single stage. <paramref name="combined"/> is the result, or
    /// <c>null</c> when both stages cancel out (Reverse pair). Returns
    /// <c>false</c> when no rule applies.
    /// </summary>
    private static bool TryFuse(IrLinqStage left, IrLinqStage right, out IrLinqStage? combined)
    {
        combined = null;
        if (left.Operator != right.Operator)
            return false;

        switch (left.Operator)
        {
            case IrLinqOperator.Where:
                return TryFuseWhere(left, right, out combined);
            case IrLinqOperator.Take:
                return TryFuseTake(left, right, out combined);
            case IrLinqOperator.Skip:
                return TryFuseSkip(left, right, out combined);
            case IrLinqOperator.Reverse:
                // Reverse takes no arguments. Two reverses cancel out
                // (combined stays null, signalling drop both).
                return left.Arguments.Count == 0 && right.Arguments.Count == 0;
            default:
                return false;
        }
    }

    // ─── Where ────────────────────────────────────────────────────────

    /// <summary>
    /// AND-combines two <c>Where</c> stages. Queryable-meta predicates
    /// merge via <see cref="IrExprBinary"/> with op <c>&amp;&amp;</c>;
    /// closure predicates compose via a synthesized lambda that invokes
    /// both originals. Stays unfused when only one side carries queryable
    /// meta (mixing tree + closure would lose either the provider's
    /// analyzable surface or the runtime closure).
    /// </summary>
    private static bool TryFuseWhere(IrLinqStage left, IrLinqStage right, out IrLinqStage? combined)
    {
        combined = null;
        if (left.Arguments.Count != 1 || right.Arguments.Count != 1)
            return false;

        var leftHasMeta = left.Queryable is not null;
        var rightHasMeta = right.Queryable is not null;
        if (leftHasMeta != rightHasMeta)
            return false;

        // The runtime helper always invokes the closure — even on the
        // queryable-meta path the provider may fall back to it when the
        // tree is not analyzable. So both branches require a safely
        // composable closure; otherwise the fused stage would silently
        // drop one predicate.
        var fusedClosure = ComposeWhereClosure(left.Arguments[0], right.Arguments[0]);
        if (fusedClosure is null)
            return false;

        if (leftHasMeta)
        {
            combined = FuseWhereWithMeta(left, right, fusedClosure);
            return true;
        }

        combined = new IrLinqStage(IrLinqOperator.Where, new[] { fusedClosure }, null);
        return true;
    }

    /// <summary>
    /// Builds the AND-combined stage for the queryable-meta case: a new
    /// <see cref="IrExprBinary"/> tree wrapping both predicate bodies and
    /// a capture list that unions both sides (deduplicated by name —
    /// captures share the runtime's <c>captures</c> dictionary). The
    /// closure argument has already been composed by the caller so the
    /// runtime fallback evaluates both predicates.
    /// </summary>
    private static IrLinqStage FuseWhereWithMeta(
        IrLinqStage left,
        IrLinqStage right,
        IrExpression fusedClosure
    )
    {
        var leftMeta = left.Queryable!;
        var rightMeta = right.Queryable!;

        var mergedTree = new IrExprBinary("&&", leftMeta.Tree, rightMeta.Tree);
        var mergedCaptures = MergeCaptures(leftMeta.Captures, rightMeta.Captures);
        var mergedMeta = new IrQueryableMeta(mergedTree, mergedCaptures);

        return new IrLinqStage(IrLinqOperator.Where, new[] { fusedClosure }, mergedMeta);
    }

    /// <summary>
    /// Synthesizes <c>(item, index) =&gt; p(item, index) &amp;&amp; q(item, index)</c>
    /// (or single-arg <c>(item) =&gt; p(item) &amp;&amp; q(item)</c> when
    /// both predicates declare one parameter). Returns null if either
    /// lambda is not in expression-body shape or its parameter list is
    /// unsupported (zero or 3+ parameters).
    /// </summary>
    private static IrExpression? ComposeWhereClosure(IrExpression left, IrExpression right)
    {
        if (
            left is not IrLambdaExpression leftLambda
            || right is not IrLambdaExpression rightLambda
        )
            return null;
        if (!IsExpressionLambda(leftLambda) || !IsExpressionLambda(rightLambda))
            return null;

        var arity = Math.Max(leftLambda.Parameters.Count, rightLambda.Parameters.Count);
        if (arity is < 1 or > 2)
            return null;

        // Parameter naming for the synthesized lambda. Items / indices
        // reuse the first lambda's parameter names so the surface stays
        // familiar in debug builds; falls back to stable synthetic names.
        var itemName = leftLambda.Parameters[0].Name;
        var indexName = arity >= 2 ? PickIndexName(leftLambda, rightLambda) : null;

        var parameters = new List<IrParameter> { CloneAsItemParameter(leftLambda.Parameters[0]) };
        if (indexName is not null)
            parameters.Add(SyntheticIndexParameter(indexName));

        var leftCall = BuildLambdaInvocation(leftLambda, itemName, indexName);
        var rightCall = BuildLambdaInvocation(rightLambda, itemName, indexName);
        var body = new IrReturnStatement(
            new IrBinaryExpression(leftCall, IrBinaryOp.LogicalAnd, rightCall)
        );

        return new IrLambdaExpression(
            parameters,
            ReturnType: null,
            Body: new IrStatement[] { body }
        );
    }

    private static bool IsExpressionLambda(IrLambdaExpression lambda) =>
        lambda.Body.Count == 1
        && lambda.Body[0] is IrReturnStatement { Value: not null }
        && !lambda.IsAsync
        && !lambda.UsesThis;

    private static string PickIndexName(IrLambdaExpression a, IrLambdaExpression b)
    {
        if (a.Parameters.Count >= 2)
            return a.Parameters[1].Name;
        if (b.Parameters.Count >= 2)
            return b.Parameters[1].Name;
        return "__i";
    }

    private static IrParameter CloneAsItemParameter(IrParameter source) =>
        new(source.Name, source.Type, HasExplicitType: source.HasExplicitType);

    private static IrParameter SyntheticIndexParameter(string name) =>
        new(name, new IrUnknownTypeRef(), HasExplicitType: false);

    /// <summary>
    /// Invokes <paramref name="lambda"/> with the synthesized lambda's
    /// item (and optional index) identifiers. The bridge sees this as a
    /// plain call whose target is the original lambda expression — the
    /// JS output is <c>((u) =&gt; …)(item)</c>.
    /// </summary>
    private static IrExpression BuildLambdaInvocation(
        IrLambdaExpression lambda,
        string itemName,
        string? indexName
    )
    {
        var args = new List<IrArgument> { new(new IrIdentifier(itemName)) };
        if (lambda.Parameters.Count >= 2 && indexName is not null)
            args.Add(new IrArgument(new IrIdentifier(indexName)));
        return new IrCallExpression(lambda, args);
    }

    /// <summary>
    /// Merges two capture lists, deduplicating by capture name
    /// (the runtime <c>captures</c> record is keyed by name). Returns
    /// <c>null</c> when both sides are empty so the meta keeps a
    /// minimal shape.
    /// </summary>
    private static IReadOnlyList<IrQueryableCapture>? MergeCaptures(
        IReadOnlyList<IrQueryableCapture>? left,
        IReadOnlyList<IrQueryableCapture>? right
    )
    {
        var leftCount = left?.Count ?? 0;
        var rightCount = right?.Count ?? 0;
        if (leftCount == 0 && rightCount == 0)
            return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<IrQueryableCapture>(leftCount + rightCount);
        AppendUnique(left, merged, seen);
        AppendUnique(right, merged, seen);
        return merged;
    }

    private static void AppendUnique(
        IReadOnlyList<IrQueryableCapture>? source,
        List<IrQueryableCapture> target,
        HashSet<string> seen
    )
    {
        if (source is null)
            return;
        foreach (var capture in source)
        {
            if (seen.Add(capture.Name))
                target.Add(capture);
        }
    }

    // ─── Take / Skip ─────────────────────────────────────────────────

    private static bool TryFuseTake(IrLinqStage left, IrLinqStage right, out IrLinqStage? combined)
    {
        combined = null;
        if (!TryGetNumericLiteralPair(left, right, out var l, out var r, out var kind))
            return false;
        // `Take(a).Take(b)` is `min(a, b)` because the second Take
        // constrains the result of the first.
        var min = l <= r ? l : r;
        if (!TryBoxNumeric(min, kind, out var boxed))
            return false;
        combined = new IrLinqStage(
            IrLinqOperator.Take,
            new IrExpression[] { new IrLiteral(boxed, kind) },
            null
        );
        return true;
    }

    private static bool TryFuseSkip(IrLinqStage left, IrLinqStage right, out IrLinqStage? combined)
    {
        combined = null;
        if (!TryGetNumericLiteralPair(left, right, out var l, out var r, out var kind))
            return false;
        // Negative arguments are valid C# but the runtime clamps them
        // to zero; `Skip(-3).Skip(2)` keeps the original semantics
        // (clamp-then-skip = Skip(2)) which does not match a numeric
        // sum (Skip(-1)). Bail to be safe.
        if (l < 0 || r < 0)
            return false;
        long sum;
        try
        {
            sum = checked(l + r);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (!TryBoxNumeric(sum, kind, out var boxed))
            return false;
        combined = new IrLinqStage(
            IrLinqOperator.Skip,
            new IrExpression[] { new IrLiteral(boxed, kind) },
            null
        );
        return true;
    }

    /// <summary>
    /// Both stages must have a single numeric-literal argument. The
    /// returned <paramref name="kind"/> picks the wider of the two
    /// literal kinds so an <c>Int64</c> literal does not silently narrow
    /// to <c>Int32</c> when merged with one.
    /// </summary>
    private static bool TryGetNumericLiteralPair(
        IrLinqStage left,
        IrLinqStage right,
        out long leftValue,
        out long rightValue,
        out IrLiteralKind kind
    )
    {
        leftValue = 0;
        rightValue = 0;
        kind = IrLiteralKind.Int32;

        if (left.Arguments.Count != 1 || right.Arguments.Count != 1)
            return false;
        if (
            !TryReadNumericLiteral(left.Arguments[0], out leftValue, out var leftKind)
            || !TryReadNumericLiteral(right.Arguments[0], out rightValue, out var rightKind)
        )
            return false;

        kind =
            leftKind == IrLiteralKind.Int64 || rightKind == IrLiteralKind.Int64
                ? IrLiteralKind.Int64
                : IrLiteralKind.Int32;
        return true;
    }

    private static bool TryReadNumericLiteral(
        IrExpression expr,
        out long value,
        out IrLiteralKind kind
    )
    {
        value = 0;
        kind = IrLiteralKind.Int32;
        if (expr is not IrLiteral literal)
            return false;
        switch (literal.Kind)
        {
            case IrLiteralKind.Int32 when literal.Value is int i32:
                value = i32;
                kind = IrLiteralKind.Int32;
                return true;
            case IrLiteralKind.Int64 when literal.Value is long i64:
                value = i64;
                kind = IrLiteralKind.Int64;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Boxes <paramref name="value"/> into the matching CLR type for
    /// <paramref name="kind"/>. Returns <c>false</c> when the value
    /// overflows <c>int.MaxValue</c>/<c>int.MinValue</c> on an
    /// <c>Int32</c> literal so the caller can bail rather than throw
    /// during transpilation.
    /// </summary>
    private static bool TryBoxNumeric(long value, IrLiteralKind kind, out object boxed)
    {
        if (kind == IrLiteralKind.Int64)
        {
            boxed = value;
            return true;
        }
        if (value > int.MaxValue || value < int.MinValue)
        {
            boxed = 0;
            return false;
        }
        boxed = (int)value;
        return true;
    }
}
