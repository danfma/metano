using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The step of a counting loop that C# shows cannot leave its type's range
/// (spec 001 FR-075): <c>i++</c> / <c>++i</c> (<c>i--</c> / <c>--i</c>) as
/// an incrementor of a <c>for</c> that declares the <c>int</c> or
/// <c>uint</c> local (or an enum with that underlying endpoint) <c>i</c>
/// in its initializer and writes it nowhere else
/// (condition, body, other incrementors, other initializers; Roslyn data
/// flow, so <c>ref</c> / <c>out</c> arguments and lambdas count), when a
/// top-level operand of the condition's <c>&amp;&amp;</c> chain (or the
/// condition itself) is a comparison in <c>i</c>'s own type that bounds the
/// step: <c>i &lt; e</c> for <c>++</c>, <c>i &gt; e</c> for <c>--</c>,
/// <c>i &lt;= e</c> when <c>e</c> ends below the type's maximum, and
/// <c>i &gt;= e</c> when <c>e</c> starts above its minimum. The condition
/// held before the body ran and <c>i</c> did not change since, so the step
/// stays in range. The proof is a fact of the C# program; the target's
/// <see cref="NumericLoweringRules.ReadsCollectionAndLoopRanges"/> decides
/// whether it drops the reduction of the step (Dart keeps it: deferred
/// after v3).
/// </summary>
internal static class BoundedCountingLoop
{
    /// <summary>The step being proven: the counter, its direction, and the rules that read its bound.</summary>
    private sealed record CountingStep(
        ILocalSymbol Counter,
        bool IsIncrement,
        NumericLoweringRules Rules
    );

    public static bool IsBoundedStep(
        IIncrementOrDecrementOperation increment,
        NumericLoweringRules rules
    )
    {
        if (!rules.ReadsCollectionAndLoopRanges)
            return false;

        if (
            increment
            is not {
                IsLifted: false,
                Target: ILocalReferenceOperation { Local: var counter },
                SemanticModel: { } semantic,
                Syntax: ExpressionSyntax { Parent: ForStatementSyntax loop } step,
            }
        )
            return false;

        var isCounter =
            rules.RepresentationOf(NumericEndpoints.Of(counter.Type))?.Kind
                is IrNumericKind.Int32
                    or IrNumericKind.UInt32
            && IsDeclaredBy(loop, counter, semantic);

        if (!isCounter || IsWrittenOutsideStep(loop, step, counter, semantic))
            return false;

        var countingStep = new CountingStep(
            counter,
            increment.Kind is OperationKind.Increment,
            rules
        );

        return loop.Condition is { } condition
            && TopLevelConjuncts(semantic.GetOperation(condition))
                .Any(conjunct => BoundsStep(conjunct, countingStep));
    }

    private static bool IsDeclaredBy(
        ForStatementSyntax loop,
        ILocalSymbol counter,
        SemanticModel semantic
    ) =>
        loop.Declaration is { } declaration
        && declaration.Variables.Any(variable =>
            SymbolEqualityComparer.Default.Equals(semantic.GetDeclaredSymbol(variable), counter)
        );

    /// <summary>
    /// Whether <paramref name="counter"/> is written in the loop anywhere but
    /// <paramref name="step"/> and its own declarator.
    /// </summary>
    private static bool IsWrittenOutsideStep(
        ForStatementSyntax loop,
        ExpressionSyntax step,
        ILocalSymbol counter,
        SemanticModel semantic
    )
    {
        var initializers = loop.Declaration!.Variables.Select(variable =>
                variable.Initializer?.Value
            )
            .OfType<ExpressionSyntax>();

        var otherIncrementors = loop.Incrementors.Where(incrementor => incrementor != step);
        var expressions = initializers.Concat(otherIncrementors);

        if (loop.Condition is { } condition)
            expressions = expressions.Append(condition);

        var expressionWrites = expressions.Any(expression =>
            Writes(semantic.AnalyzeDataFlow(expression), counter)
        );

        return expressionWrites || Writes(semantic.AnalyzeDataFlow(loop.Statement), counter);
    }

    private static bool Writes(DataFlowAnalysis? flow, ILocalSymbol counter) =>
        flow is not { Succeeded: true }
        || flow.WrittenInside.Contains(counter, SymbolEqualityComparer.Default);

    /// <summary>The condition, or the operands of its top-level <c>&amp;&amp;</c> chain.</summary>
    private static IEnumerable<IOperation> TopLevelConjuncts(IOperation? condition) =>
        condition switch
        {
            null => [],
            IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd } and =>
                TopLevelConjuncts(and.LeftOperand).Concat(TopLevelConjuncts(and.RightOperand)),
            _ => [condition],
        };

    /// <summary>
    /// Whether <paramref name="conjunct"/> compares the bare
    /// counter of <paramref name="step"/>, in its own type, with a bound that
    /// keeps the step in range.
    /// </summary>
    private static bool BoundsStep(IOperation conjunct, CountingStep step)
    {
        var counter = step.Counter;

        if (
            conjunct
            is not IBinaryOperation
            {
                OperatorMethod: null,
                LeftOperand: var left,
                RightOperand: var right,
            } comparison
        )
            return false;

        var computedInCounterType =
            SymbolEqualityComparer.Default.Equals(left.Type, counter.Type)
            && SymbolEqualityComparer.Default.Equals(right.Type, counter.Type);

        if (!computedInCounterType)
            return false;

        if (IsCounter(left, counter))
            return Bounds(comparison.OperatorKind, right, step);

        return IsCounter(right, counter) && Bounds(Mirrored(comparison.OperatorKind), left, step);
    }

    private static bool IsCounter(IOperation operand, ILocalSymbol counter) =>
        operand is ILocalReferenceOperation { Local: var local }
        && SymbolEqualityComparer.Default.Equals(local, counter);

    /// <summary>Whether <c>counter op bound</c> keeps the step in range.</summary>
    private static bool Bounds(BinaryOperatorKind operatorKind, IOperation bound, CountingStep step)
    {
        var type = IntegerValueRanges.OfType(step.Counter.Type, step.Rules);
        var values = IntegerValueRanges.Of(bound, step.Rules);

        return (operatorKind, step.IsIncrement) switch
        {
            (BinaryOperatorKind.LessThan, true) => true,
            (BinaryOperatorKind.GreaterThan, false) => true,
            (BinaryOperatorKind.LessThanOrEqual, true) => values?.Max < type?.Max,
            (BinaryOperatorKind.GreaterThanOrEqual, false) => values?.Min > type?.Min,
            _ => false,
        };
    }

    /// <summary>The operator of <c>b op a</c> read as <c>a op' b</c>.</summary>
    private static BinaryOperatorKind Mirrored(BinaryOperatorKind operatorKind) =>
        operatorKind switch
        {
            BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
            BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
            BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual,
            BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
            _ => operatorKind,
        };
}
