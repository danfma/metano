using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The element updates through the indexer of a stage 011c collection (spec
/// 011 FR-010, data-model § 2): <c>r[i] op= v</c>, <c>r[i]++</c> / <c>--</c>
/// (prefix and postfix), <c>r[i] ??= v</c> and their <c>r[^n]</c> forms become
/// one <see cref="IrIndexerUpdateExpression"/>. The value written is computed
/// by the ordinary compound-assignment and increment lowerings, run once over
/// a placeholder for the current element (<see cref="IndexerUpdateProbe"/>),
/// so every operator, increment-constant and overflow rule of the element
/// type applies unchanged. A target that keeps native collections
/// (<see cref="IrExtractionContext.KeepsNativeCollections"/>) never gets the
/// node: it keeps the 2.x element-access lowering.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The update site whose value-written lowering is being computed:
    /// extracting <see cref="Place"/> yields <see cref="Current"/>, and the
    /// update hook skips <see cref="Node"/> itself.
    /// </summary>
    private sealed record IndexerUpdateProbe(
        ExpressionSyntax Node,
        ExpressionSyntax Place,
        IrIdentifier Current
    );

    private IndexerUpdateProbe? _indexerUpdateProbe;

    private int _nextIndexerUpdate;

    /// <summary>
    /// <c>r[i] op= v</c> / <c>r[i] ??= v</c> on a stage 011c collection, or
    /// <c>null</c> for any other assignment.
    /// </summary>
    private IrExpression? TryExtractCollectionElementUpdate(AssignmentExpressionSyntax assign)
    {
        if (assign.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return null;

        return TryExtractCollectionElementUpdate(assign, assign.Left, UpdateResultOf(assign), () =>
            WithEnumResultType(assign, ExtractAssignment(assign))
        );
    }

    /// <summary>
    /// <c>r[i]++</c>, <c>++r[i]</c>, <c>r[i]--</c> or <c>--r[i]</c> on a stage
    /// 011c collection, or <c>null</c> for any other operand. The value written
    /// is the prefix form's.
    /// </summary>
    private IrExpression? TryExtractCollectionElementIncrement(
        ExpressionSyntax node,
        ExpressionSyntax operand,
        bool isPrefix
    ) =>
        TryExtractCollectionElementUpdate(
            node,
            operand,
            isPrefix ? IrIndexerUpdateResult.NewValue : IrIndexerUpdateResult.OldValue,
            () => WithEnumResultType(node, ExtractUnary(node, isPrefix: true))
        );

    /// <summary>
    /// The inner write of a null-conditional compound assignment
    /// (<c>x?.Items[i] += v</c>, <c>list?[i] ??= v</c>) whose rebound target is
    /// an element of a stage 011c collection; <c>null</c> for any other target.
    /// </summary>
    private IrExpression? TryExtractConditionalElementUpdate(
        IrExpression rebound,
        AssignmentExpressionSyntax assign,
        Func<IrExpression, IrExpression> store
    )
    {
        if (assign.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return null;

        if (ReboundCollectionElementOf(rebound) is not { } place)
            return null;

        return UpdateOf(assign, place, UpdateResultOf(assign), store);
    }

    /// <summary>What the compound assignment <paramref name="assign"/> yields.</summary>
    private static IrIndexerUpdateResult UpdateResultOf(AssignmentExpressionSyntax assign) =>
        assign.IsKind(SyntaxKind.CoalesceAssignmentExpression)
            ? IrIndexerUpdateResult.Coalesce
            : IrIndexerUpdateResult.NewValue;

    /// <summary>
    /// The collection element a null-conditional write's rebound target
    /// addresses (an element access on a stage 011c collection); <c>null</c>
    /// for any other target.
    /// </summary>
    private static IrCollectionElement? ReboundCollectionElementOf(IrExpression rebound) =>
        rebound is IrElementAccess { TargetType: IrCollectionTypeRef type } element
            ? new IrCollectionElement(element.Target, element.Index, FromEnd: false, type)
            : null;

    private IrExpression? TryExtractCollectionElementUpdate(
        ExpressionSyntax node,
        ExpressionSyntax placeSyntax,
        IrIndexerUpdateResult result,
        Func<IrExpression> storeOfProbe
    )
    {
        if (_indexerUpdateProbe?.Node == node)
            return null;

        if (CollectionElementOf(placeSyntax) is not { } place)
            return null;

        return UpdateOf(node, place, result, current =>
            Probing(node, placeSyntax, (IrIdentifier)current, storeOfProbe)
        );
    }

    /// <summary>
    /// The update node of <paramref name="node"/>: <paramref name="store"/>
    /// lowers the write over the current element, and the value it stores
    /// becomes <see cref="IrIndexerUpdateExpression.NewValue"/>. A lowering
    /// that reports the site keeps its placeholder; a store shape the node
    /// cannot hold is reported.
    /// </summary>
    private IrExpression UpdateOf(
        ExpressionSyntax node,
        IrCollectionElement place,
        IrIndexerUpdateResult result,
        Func<IrExpression, IrExpression> store
    )
    {
        var current = new IrIdentifier($"v${_nextIndexerUpdate++}");
        var stored = store(current);

        if (IsUnsupportedPlaceholder(stored))
            return stored;

        if (StoredValueOf(node, stored, current.Name) is not { } newValue)
            return ReportUnsupported(
                node,
                "CollectionElementUpdate",
                $"'{node}' is not supported: the element update has no single value to write."
            );

        return new IrIndexerUpdateExpression(place, current.Name, newValue, result)
        {
            Location = SourceLocationOf(node),
        };
    }

    private IrExpression Probing(
        ExpressionSyntax node,
        ExpressionSyntax placeSyntax,
        IrIdentifier current,
        Func<IrExpression> extract
    )
    {
        var enclosing = _indexerUpdateProbe;
        _indexerUpdateProbe = new IndexerUpdateProbe(node, placeSyntax, current);

        try
        {
            return extract();
        }
        finally
        {
            _indexerUpdateProbe = enclosing;
        }
    }

    /// <summary>
    /// The placeholder of the element being updated, when
    /// <paramref name="expression"/> is the place a probe is computing.
    /// </summary>
    private IrIdentifier? ProbedElementOf(ExpressionSyntax expression) =>
        _indexerUpdateProbe is { } probe && expression == probe.Place ? probe.Current : null;

    /// <summary>
    /// The value a lowered store over <paramref name="current"/> writes:
    /// <c>current = v</c> stores <c>v</c>, <c>current op= v</c> stores
    /// <c>current op v</c>, <c>current ??= v</c> stores <c>v</c> (written only
    /// when the element is null), <c>++current</c> stores <c>current + 1</c>;
    /// <c>null</c> for any other shape.
    /// </summary>
    private IrExpression? StoredValueOf(
        ExpressionSyntax node,
        IrExpression stored,
        string current
    ) =>
        stored switch
        {
            IrUserOperatorStore user => StoredValueOf(node, user.ReadModifyWrite, current),
            IrBinaryExpression { Left: IrIdentifier left } binary when left.Name == current =>
                StoredBinaryValueOf(node, binary),
            IrUnaryExpression
            {
                Operator: IrUnaryOp.Increment or IrUnaryOp.Decrement,
                Operand: IrIdentifier operand,
            } step when operand.Name == current => new IrBinaryExpression(
                operand,
                StepOperatorOf(step.Operator),
                OneOf(IncrementedOperand(node)!)
            )
            {
                EnumResultType = step.EnumResultType,
            },
            _ => null,
        };

    private IrExpression? StoredBinaryValueOf(ExpressionSyntax node, IrBinaryExpression binary) =>
        binary.Operator switch
        {
            IrBinaryOp.Assign or IrBinaryOp.NullCoalescingAssign => binary.Right,
            _ when node is AssignmentExpressionSyntax assign
                    && MapCompoundAssignmentOperator(assign) is { } op => binary with
            {
                Operator = op,
            },
            _ => null,
        };

    /// <summary>
    /// The element of a stage 011c collection that <paramref name="syntax"/>
    /// writes through the collection's own indexer (<c>r[i]</c>, <c>r[^n]</c>),
    /// with its receiver and index extracted in source order; <c>null</c> for
    /// any other place. A 64-bit index is reported: the update node has no
    /// index conversion.
    /// </summary>
    private IrCollectionElement? CollectionElementOf(ExpressionSyntax syntax)
    {
        if (CollectionIndexerAccessOf(syntax) is not { } element)
            return null;

        if (CollectionReceiverTypeOf(element.Expression) is not { } type)
            return null;

        var operand = Unparenthesize(element.ArgumentList.Arguments[0].Expression);
        var receiver = Extract(element.Expression);

        if (AsIndexFromEnd(operand) is { } fromEnd)
            return new IrCollectionElement(receiver, Extract(fromEnd.Operand), true, type);

        if (ArrayIndexKind(element.Expression, element.ArgumentList) is not null)
            return null;

        return new IrCollectionElement(receiver, Extract(operand), false, type);
    }

    /// <summary>
    /// <paramref name="syntax"/> as a one-argument access through the indexer
    /// a stage 011c collection declares; <c>null</c> for any other place.
    /// </summary>
    private ElementAccessExpressionSyntax? CollectionIndexerAccessOf(ExpressionSyntax syntax) =>
        Unparenthesize(syntax)
            is ElementAccessExpressionSyntax { ArgumentList.Arguments: [_] } element
        && IsCollectionIndexer(_semantic.GetSymbolInfo(element).Symbol)
            ? element
            : null;

    /// <summary>
    /// Whether <paramref name="symbol"/> is the indexer a stage 011c
    /// collection or collection interface declares (a C# 14 extension indexer
    /// or a user indexer is not).
    /// </summary>
    private static bool IsCollectionIndexer(ISymbol? symbol) =>
        symbol is IPropertySymbol { IsIndexer: true, ContainingType: { } containing }
        && CollectionKindResolver.KindOf(containing.OriginalDefinition) is not null;

    /// <summary>
    /// The collection type a receiver of <paramref name="receiverSyntax"/> is
    /// read and written as: its own stage 011c collection type, or the
    /// collection class a user class derives from (<c>class IntList :
    /// List&lt;int&gt;</c> indexes as a <c>List&lt;int&gt;</c>); <c>null</c> for
    /// any other receiver.
    /// </summary>
    private IrCollectionTypeRef? CollectionReceiverTypeOf(ExpressionSyntax receiverSyntax)
    {
        for (
            var type =
                _semantic.GetTypeInfo(receiverSyntax).Type?.UnderlyingValueType()
                as INamedTypeSymbol;
            type is not null;
            type = type.BaseType
        )
        {
            if (CollectionKindResolver.KindOf(type) is not null)
                return IrTypeRefMapper.Map(
                        type.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                        _originResolver,
                        _target
                    ) as IrCollectionTypeRef;
        }

        return null;
    }

    /// <summary>
    /// The receiver type an element access records (B2) for a target whose
    /// collections are runtime classes: the collection a user class derives
    /// from takes the place of the class, so its elements go through
    /// <c>get</c> / <c>set</c>; otherwise the receiver's own type.
    /// </summary>
    private IrTypeRef? ElementReceiverTypeOf(ExpressionSyntax receiverSyntax) =>
        CollectionReceiverTypeOf(receiverSyntax) ?? MapExpressionType(receiverSyntax);
}
