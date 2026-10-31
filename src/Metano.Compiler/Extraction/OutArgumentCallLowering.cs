using Metano.Compiler.Analysis;
using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Places the results of the <see cref="IrOutArgumentCall"/>s a statement
/// makes, keeping C#'s evaluation order and scoping. Runs on each statement
/// right after extraction, so the statement lists nested in it (branches, loop
/// bodies, lambda bodies) are already lowered.
/// <list type="bullet">
///   <item><b>Bound ahead.</b> A call the statement evaluates unconditionally,
///   before anything whose value the call could change, is bound by an
///   <see cref="IrTupleDeconstruction"/> placed before the statement
///   (<c>[$found0, v] = call</c>), and the call is replaced by the result
///   name. <c>var ok = call;</c> binds the result to <c>ok</c> directly;
///   <c>call;</c> drops it.</item>
///   <item><b>Loop conditions.</b> A <c>while</c> / <c>for</c> condition (and
///   a <c>do</c> condition when the body has no <c>continue</c>) is evaluated
///   at the top (bottom) of a <c>while (true)</c> / <c>for (init; ; inc)</c>
///   body followed by <c>if (!condition) break;</c>, so each iteration binds
///   fresh locals, as C# scopes them.</item>
///   <item><b>In place.</b> Any other call (a short-circuit operand, a
///   conditional branch) stays an <see cref="IrOutArgumentCall"/>: each
///   <c>out var</c> local it declares becomes an uninitialized, typed
///   <see cref="IrVariableDeclaration"/> before the statement, which the call
///   assigns when it runs.</item>
/// </list>
/// A loop that gains declarations is wrapped in an
/// <see cref="IrBlockStatement"/>, since C# scopes its header variables to
/// the loop.
/// </summary>
internal sealed partial class OutArgumentCallLowering
{
    private int _nextTemporary;

    /// <summary>
    /// The statements that replace <paramref name="statement"/>: its bindings
    /// and declarations, followed by the rewritten statement.
    /// <paramref name="following"/> are the statements after it in the same
    /// scope, which tell whether an <c>out var</c> local is read after it.
    /// </summary>
    public IReadOnlyList<IrStatement> Lower(
        IrStatement statement,
        IReadOnlyList<IrStatement>? following = null
    )
    {
        if (!MakesOutArgumentCalls(statement))
            return [statement];

        return statement switch
        {
            IrIfStatement ifs when SplitAtGuardedCall(ifs, following ?? []) is { } split => split,
            IrWhileStatement loop => [LowerWhile(loop)],
            IrDoWhileStatement loop when !ContinuesLoop(loop.Body) => [LowerDoWhile(loop)],
            IrForStatement loop when CanRestructureFor(loop) => [LowerFor(loop)],
            IrForStatement loop => [LowerForInPlace(loop)],
            IrWhileStatement or IrDoWhileStatement or IrForEachStatement =>
            [
                new IrBlockStatement(LowerInPlace(statement)),
            ],
            _ => LowerInPlace(statement),
        };
    }

    /// <summary>
    /// <c>if (a &amp;&amp; call(out var v) &amp;&amp; b) body</c> without an
    /// <c>else</c> → <c>if (a) { const [$found0, v] = call; if ($found0 &amp;&amp; b) body }</c>:
    /// the call runs only when <c>a</c> holds, as before, and its values are
    /// bound before <c>b</c> and the body read them, like every other bound
    /// call (a backend can then relate them to the result). Only when no
    /// statement after the <c>if</c> names a local the call declares, since
    /// the local now lives inside the outer branch.
    /// </summary>
    private IReadOnlyList<IrStatement>? SplitAtGuardedCall(
        IrIfStatement ifs,
        IReadOnlyList<IrStatement> following
    )
    {
        if (ifs.Else is not null)
            return null;

        var conjuncts = ConjunctsOf(ifs.Condition);
        var firstOutCallIndex = conjuncts.FindIndex(StartsWithOutArgumentCall);

        if (firstOutCallIndex <= 0)
            return null;

        var guardedConjuncts = conjuncts.Skip(firstOutCallIndex).ToList();

        if (IsNamedIn(DeclaredLocals(guardedConjuncts), following))
            return null;

        var guard = AndAll(conjuncts.Take(firstOutCallIndex).ToList());
        var inner = Lower(new IrIfStatement(AndAll(guardedConjuncts), ifs.Then), following);

        // The guard may still make calls of its own that need binding.
        return Lower(new IrIfStatement(guard, inner));
    }

    private static bool IsNamedIn(IEnumerable<string> locals, IReadOnlyList<IrStatement> statements)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var statement in statements)
            CollectNames(statement, names);

        return locals.Any(names.Contains);
    }

    private static List<IrExpression> ConjunctsOf(IrExpression condition) =>
        condition is IrBinaryExpression { Operator: IrBinaryOp.LogicalAnd } and
            ? [.. ConjunctsOf(and.Left), and.Right]
            : [condition];

    private static IrExpression AndAll(IReadOnlyList<IrExpression> conjuncts) =>
        conjuncts
            .Skip(1)
            .Aggregate(
                conjuncts[0],
                (left, right) => new IrBinaryExpression(left, IrBinaryOp.LogicalAnd, right)
            );

    private static bool StartsWithOutArgumentCall(IrExpression conjunct) =>
        conjunct
            is IrOutArgumentCall { ResultType: not null }
                or IrUnaryExpression
                {
                    Operator: IrUnaryOp.LogicalNot,
                    Operand: IrOutArgumentCall { ResultType: not null },
                };

    private static IEnumerable<string> DeclaredLocals(IEnumerable<IrExpression> expressions) =>
        OutLocalsDeclaredIn(expressions).Select(local => local.Name);

    /// <summary>
    /// The <c>out var</c> locals the calls in <paramref name="expressions"/>
    /// declare.
    /// </summary>
    private static IEnumerable<IrOutLocalDeclaration> OutLocalsDeclaredIn(
        IEnumerable<IrExpression> expressions
    ) =>
        expressions
            .SelectMany(OutCallsIn)
            .SelectMany(call => call.OutArguments)
            .Select(o => o.Target)
            .OfType<IrOutLocalDeclaration>();

    /// <summary>
    /// Adds every identifier and declared name in <paramref name="statement"/>,
    /// nested statements and lambdas included — a conservative "is this name
    /// used" test (a same-named local of a sibling scope counts too).
    /// </summary>
    private static void CollectNames(IrStatement statement, HashSet<string> names)
    {
        if (statement is IrVariableDeclaration declaration)
            names.Add(declaration.Name);

        foreach (var expression in IrNodeChildren.ExpressionsOf(statement))
            CollectNames(expression, names);

        foreach (var nested in IrNodeChildren.StatementsOf(statement))
            CollectNames(nested, names);
    }

    private static void CollectNames(IrExpression expression, HashSet<string> names)
    {
        if (expression is IrIdentifier identifier)
            names.Add(identifier.Name);

        foreach (var local in DeclaredLocals([expression]))
            names.Add(local);

        foreach (var child in IrNodeChildren.ExpressionsOf(expression))
            CollectNames(child, names);

        foreach (var nested in IrNodeChildren.StatementsOf(expression))
            CollectNames(nested, names);
    }

    private List<IrStatement> LowerInPlace(IrStatement statement)
    {
        var prelude = new List<IrStatement>();
        var rewritten = BindStatementHead(statement, prelude);
        var emitted = rewritten is null ? prelude : [.. prelude, rewritten];

        return [.. DeclarationsForCallsLeftInPlace(emitted), .. emitted];
    }

    /// <summary>
    /// Binds the calls in <paramref name="statement"/>'s own expressions that
    /// run first, and returns the statement left to emit (<c>null</c> when the
    /// binding replaces it entirely).
    /// </summary>
    private IrStatement? BindStatementHead(IrStatement statement, List<IrStatement> prelude)
    {
        var head = new HeadEvaluation(this, prelude);

        switch (statement)
        {
            case IrExpressionStatement { Expression: IrOutArgumentCall call }:
                Bind(WithBoundParts(call, head), ResultSlot.Dropped, prelude);
                return null;

            case IrVariableDeclaration
            {
                Initializer: IrOutArgumentCall { ResultType: not null } call
            } declaration:
                Bind(WithBoundParts(call, head), ResultSlot.Named(declaration.Name), prelude);
                return null;

            case IrReturnStatement { Value: IrOutArgumentCall { ResultType: null } call }:
                Bind(WithBoundParts(call, head), ResultSlot.Dropped, prelude);
                return new IrReturnStatement();
        }

        return statement switch
        {
            IrExpressionStatement es => es with { Expression = head.Bind(es.Expression) },
            IrVariableDeclaration { Initializer: { } initializer } vd => vd with
            {
                Initializer = head.Bind(initializer),
            },
            IrReturnStatement { Value: { } value } ret => ret with { Value = head.Bind(value) },
            IrThrowStatement th => th with { Expression = head.Bind(th.Expression) },
            IrIfStatement ifs => ifs with { Condition = head.Bind(ifs.Condition) },
            IrSwitchStatement sw => sw with { Expression = head.Bind(sw.Expression) },
            IrForEachStatement fe => fe with { Collection = head.Bind(fe.Collection) },
            _ => statement,
        };
    }

    private static IrOutArgumentCall WithBoundParts(IrOutArgumentCall call, HeadEvaluation head) =>
        call with
        {
            Call = head.BindCallParts(call.Call),
        };

    /// <summary>
    /// <c>while (c) body</c> → <c>while (true) { bindings; if (!c) break; body }</c>.
    /// </summary>
    private IrWhileStatement LowerWhile(IrWhileStatement loop) =>
        new(
            new IrLiteral(true, IrLiteralKind.Boolean),
            [.. ExitUnless(loop.Condition), .. loop.Body]
        );

    /// <summary>
    /// <c>do body while (c)</c> → <c>while (true) { body; bindings; if (!c) break; }</c>.
    /// Only for a body without <c>continue</c>, which would skip the condition.
    /// </summary>
    private IrWhileStatement LowerDoWhile(IrDoWhileStatement loop) =>
        new(
            new IrLiteral(true, IrLiteralKind.Boolean),
            [.. loop.Body, .. ExitUnless(loop.Condition)]
        );

    /// <summary>
    /// <c>for (init; c; inc) body</c> → <c>for (init; ; inc) { bindings; if (!c) break; body }</c>,
    /// wrapped in a block with whatever the initializer's own calls need.
    /// </summary>
    private IrStatement LowerFor(IrForStatement loop)
    {
        var (header, ahead) = SplitLoweredInitializers(loop);

        var body = loop.Condition is null
            ? loop.Body
            : [.. ExitUnless(loop.Condition), .. loop.Body];

        var restructured = loop with { Initializers = header, Condition = null, Body = body };

        return ahead is [] ? restructured : new IrBlockStatement([.. ahead, restructured]);
    }

    /// <summary>
    /// A <c>for</c> loop whose condition cannot move into the body: its
    /// initializer is bound ahead as in <see cref="LowerFor"/>, and the
    /// condition and increment calls stay in place, all in one block.
    /// </summary>
    private IrBlockStatement LowerForInPlace(IrForStatement loop)
    {
        var (header, ahead) = SplitLoweredInitializers(loop);

        return new IrBlockStatement([
            .. ahead,
            .. LowerInPlace(loop with { Initializers = header }),
        ]);
    }

    /// <summary>
    /// Lowers the loop's initializers and splits them between the loop
    /// header and the statements bound ahead of the loop: they stay in the
    /// header only when each declarator lowered to a single plain
    /// declaration. Anything else (a binding an initializer's call needs, an
    /// initializer expression) moves ahead as a whole, keeping source order.
    /// </summary>
    private (List<IrStatement> Header, List<IrStatement> Ahead) SplitLoweredInitializers(
        IrForStatement loop
    )
    {
        var lowered = loop.Initializers.Select(initializer => Lower(initializer)).ToList();
        var flattened = lowered.SelectMany(statements => statements).ToList();

        return lowered.All(FitsInLoopHeader) ? (flattened, []) : ([], flattened);
    }

    private static bool FitsInLoopHeader(IReadOnlyList<IrStatement> lowered) =>
        lowered is [IrVariableDeclaration];

    /// <summary>
    /// The statements that evaluate <paramref name="condition"/> inside a loop
    /// body and leave the loop when it is false.
    /// </summary>
    private List<IrStatement> ExitUnless(IrExpression condition)
    {
        var exit = new IrIfStatement(
            new IrUnaryExpression(IrUnaryOp.LogicalNot, condition),
            [new IrBreakStatement()]
        );

        return LowerInPlace(exit);
    }

    /// <summary>
    /// A <c>for</c> loop can move its condition into the body unless the
    /// increment reads a local the condition declares (it would fall out of
    /// scope there).
    /// </summary>
    private static bool CanRestructureFor(IrForStatement loop)
    {
        if (loop.Incrementors.Any(ContainsOutArgumentCall))
            return false;

        if (loop.Condition is null || !ContainsOutArgumentCall(loop.Condition))
            return true;

        var declared = DeclaredLocals([loop.Condition]).ToHashSet(StringComparer.Ordinal);

        return !loop.Incrementors.SelectMany(IdentifiersIn).Any(declared.Contains);
    }

    /// <summary>
    /// Declares, ahead of the statement, every <c>out var</c> local of a call
    /// that stays in place.
    /// </summary>
    private static IEnumerable<IrStatement> DeclarationsForCallsLeftInPlace(
        IEnumerable<IrStatement> statements
    ) =>
        OutLocalsDeclaredIn(statements.SelectMany(IrNodeChildren.ExpressionsOf))
            .Where(local => !statements.Any(s => BindsName(s, local.Name)))
            .DistinctBy(local => local.Name)
            .Select(local => new IrVariableDeclaration(local.Name, local.Type, Initializer: null));

    /// <summary>
    /// <c>const [result, out1, …] = call;</c> plus one assignment per existing
    /// <c>out</c> target, which binds through a temporary.
    /// </summary>
    private IrIdentifier? Bind(IrOutArgumentCall call, ResultSlot result, List<IrStatement> prelude)
    {
        var resultName = call.ResultType is null ? null : result.NameFor(call, this);
        var assignments = new List<IrStatement>();

        var elements = call
            .OutArguments.Select(o => new IrDeconstructionElement(
                ElementName(o.Target, assignments)
            ))
            .ToList();

        if (call.ResultType is not null)
            elements.Insert(0, new IrDeconstructionElement(resultName));

        TrimTrailingDiscards(elements);

        prelude.Add(
            elements.Count == 0
                ? new IrExpressionStatement(call)
                : new IrTupleDeconstruction(elements, call, IsConst: false)
        );

        prelude.AddRange(assignments);

        return resultName is null ? null : new IrIdentifier(resultName);
    }

    private static bool BindsName(IrStatement statement, string name) =>
        statement is IrTupleDeconstruction td && td.Elements.Any(e => e.Name == name);

    /// <summary>A trailing discard needs no hole: <c>[a, ]</c> is <c>[a]</c>.</summary>
    private static void TrimTrailingDiscards(List<IrDeconstructionElement> elements)
    {
        while (elements is [.., { Name: null }])
            elements.RemoveAt(elements.Count - 1);
    }

    private string? ElementName(IrOutTarget target, List<IrStatement> assignments)
    {
        switch (target)
        {
            case IrOutLocalDeclaration local:
                return local.Name;

            case IrOutAssignment assignment:
                var temporary = NextTemporary("$out");

                assignments.Add(
                    new IrExpressionStatement(
                        new IrBinaryExpression(
                            assignment.Destination,
                            IrBinaryOp.Assign,
                            new IrIdentifier(temporary)
                        )
                    )
                );

                return temporary;

            default:
                return null;
        }
    }

    private string NextTemporary(string prefix) => $"{prefix}{_nextTemporary++}";

    private static bool MakesOutArgumentCalls(IrStatement statement) =>
        IrNodeChildren.ExpressionsOf(statement).Any(ContainsOutArgumentCall)
        || statement is IrForStatement loop && loop.Initializers.Any(MakesOutArgumentCalls);

    private static bool ContainsOutArgumentCall(IrExpression expression) =>
        OutCallsIn(expression).Any();

    /// <summary>
    /// The out-argument calls in <paramref name="expression"/>, outside nested
    /// lambdas (whose bodies are lowered on their own):
    /// <see cref="IrNodeChildren.ExpressionsOf(IrExpression)"/> never yields a
    /// lambda's body.
    /// </summary>
    private static IEnumerable<IrOutArgumentCall> OutCallsIn(IrExpression expression)
    {
        if (expression is IrOutArgumentCall call)
            yield return call;

        foreach (var child in IrNodeChildren.ExpressionsOf(expression))
        {
            foreach (var nested in OutCallsIn(child))
                yield return nested;
        }
    }

    private static IEnumerable<string> IdentifiersIn(IrExpression expression) =>
        expression is IrIdentifier identifier
            ? [identifier.Name]
            : IrNodeChildren.ExpressionsOf(expression).SelectMany(IdentifiersIn);

    /// <summary>
    /// Whether <paramref name="body"/> holds a <c>continue</c> for the loop it
    /// belongs to (not one of a nested loop).
    /// </summary>
    private static bool ContinuesLoop(IEnumerable<IrStatement> body) =>
        body.Any(statement =>
            statement switch
            {
                IrContinueStatement => true,
                IrWhileStatement or IrDoWhileStatement or IrForStatement or IrForEachStatement =>
                    false,
                _ => ContinuesLoop(IrNodeChildren.StatementsOf(statement)),
            }
        );

    /// <summary>What the binding does with the callee's return value.</summary>
    private abstract record ResultSlot
    {
        public static readonly ResultSlot Dropped = new DroppedSlot();

        public static readonly ResultSlot Temporary = new TemporarySlot();

        public static ResultSlot Named(string name) => new NamedSlot(name);

        public abstract string? NameFor(IrOutArgumentCall call, OutArgumentCallLowering lowering);

        private sealed record DroppedSlot : ResultSlot
        {
            public override string? NameFor(
                IrOutArgumentCall call,
                OutArgumentCallLowering lowering
            ) => null;
        }

        private sealed record TemporarySlot : ResultSlot
        {
            public override string NameFor(
                IrOutArgumentCall call,
                OutArgumentCallLowering lowering
            ) =>
                // `$` keeps the name clear of every C# identifier; the
                // TypeScript result-type labels (OutResultTypeRef.For) use
                // the same found/result words without it.
                lowering.NextTemporary(
                    call.ResultType is IrPrimitiveTypeRef { Primitive: IrPrimitive.Boolean }
                        ? "$found"
                        : "$result"
                );
        }

        private sealed record NamedSlot(string Name) : ResultSlot
        {
            public override string NameFor(
                IrOutArgumentCall call,
                OutArgumentCallLowering lowering
            ) => Name;
        }
    }
}
