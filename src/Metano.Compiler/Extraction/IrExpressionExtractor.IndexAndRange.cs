using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// C# 8 index-from-end (<c>x[^n]</c>) and range (<c>x[a..b]</c>) element
/// accesses. They are desugared the way Roslyn itself lowers them:
/// <c>^n</c> becomes <c>length - n</c>, where <c>length</c> reads the
/// receiver's own C# length member (<c>Array.Length</c>,
/// <c>string.Length</c>, <c>List&lt;T&gt;.Count</c>, …) so every backend
/// lowers it through its ordinary BCL member mapping, and a range becomes an
/// <see cref="IrSliceExpression"/>.
///
/// <para>
/// A receiver that is not a <see cref="IrEvaluationTraits.IsReusableReceiver">reusable
/// receiver</see> is bound once through an <see cref="IrLetExpression"/>, so
/// its side effects run exactly once even though the desugared form reads it
/// twice (indexing and length). Writes through such a receiver
/// (<c>Make()[^1] = v</c>) are reported instead: the let-binding is an
/// expression, not an assignable location.
/// </para>
///
/// <para>
/// Out-of-range accesses follow the target's indexing semantics, as for any
/// other element access, where C# would throw: <c>x[^0]</c> yields
/// <c>undefined</c> in TypeScript, an inverted range an empty slice, a
/// start before the beginning (<c>a[^10..]</c> on 5 elements, so
/// <c>slice(-5)</c>) counts from the end like any negative <c>slice</c>
/// argument, and an end past the length is clamped.
/// </para>
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string IndexTypeName = "System.Index";

    private const string RangeTypeName = "System.Range";

    private const string ListTypeName = "System.Collections.Generic.List`1";

    /// <summary>
    /// One extracted range bound: a plain offset, or the <c>n</c> of a
    /// from-end <c>^n</c> bound.
    /// </summary>
    private sealed record RangeBound(IrExpression Value, bool FromEnd);

    /// <summary>
    /// Lowers an element access whose single argument is a
    /// <see cref="System.Index"/> or <see cref="System.Range"/> value.
    /// Returns <c>null</c> for every other element access, which keeps the
    /// ordinary indexer lowering.
    /// </summary>
    private IrExpression? TryExtractIndexOrRangeAccess(
        ExpressionSyntax elem,
        ExpressionSyntax receiverSyntax,
        BracketedArgumentListSyntax arguments
    )
    {
        if (arguments.Arguments is not [var argument])
            return null;

        var argumentType = _semantic.GetTypeInfo(argument.Expression).Type;
        var isIndex = IsWellKnownType(argumentType, IndexTypeName);
        var isRange = IsWellKnownType(argumentType, RangeTypeName);

        if (!isIndex && !isRange)
            return null;

        var access = _semantic.GetOperation(elem);
        var length = LengthMemberOf(access);
        var operand = Unparenthesize(argument.Expression);

        if (length is not null && isIndex && AsIndexFromEnd(operand) is { } indexFromEnd)
            return ExtractIndexFromEndAccess(elem, receiverSyntax, indexFromEnd, length);

        if (
            length is not null
            && operand is RangeExpressionSyntax range
            && IsLowerableRange(access, range)
        )
            return ExtractRangeAccess(receiverSyntax, range, length);

        return ReportUnsupported(
            elem,
            isIndex ? "IndexElementAccess" : "RangeElementAccess",
            DescribeUnsupportedIndexOrRange(elem, receiverSyntax, operand, access)
        );
    }

    private string DescribeUnsupportedIndexOrRange(
        ExpressionSyntax elem,
        ExpressionSyntax receiverSyntax,
        ExpressionSyntax operand,
        IOperation? access
    )
    {
        var receiverType = _semantic.GetTypeInfo(receiverSyntax).Type?.ToDisplayString();

        return operand switch
        {
            _ when AsIndexFromEnd(operand) is not null =>
                $"Index-from-end access '{elem}' is not supported on '{receiverType}': only "
                    + "arrays, strings and lists are lowered.",
            RangeExpressionSyntax when !IsSliceableRangeAccess(access) =>
                $"Range access '{elem}' is not supported on '{receiverType}': only arrays, "
                    + "strings and List<T> are sliced; a custom Slice(start, length) method does "
                    + "not match the target's slice(start, end).",
            RangeExpressionSyntax =>
                $"Range access '{elem}' is not supported: each bound must be omitted, an "
                    + "inline '^n', or an int offset, not an Index value held in a variable.",
            _ => $"Element access '{elem}' is not supported: an Index or Range value held in a "
                + "variable cannot be lowered. Write the '^n' index or 'a..b' range inline.",
        };
    }

    /// <summary>
    /// <c>x[^n]</c> → <c>x[x.length - n]</c>. C# evaluates the receiver, then
    /// the offset, then reads the length; an offset with side effects is
    /// let-bound so it still runs before the length read (it could change it).
    /// A write target keeps the offset inline because the let-binding is not
    /// an assignable location — there the length is read before the offset.
    /// </summary>
    private IrExpression ExtractIndexFromEndAccess(
        ExpressionSyntax elem,
        ExpressionSyntax receiverSyntax,
        PrefixUnaryExpressionSyntax indexFromEnd,
        ISymbol length
    )
    {
        var receiver = Extract(receiverSyntax);
        var isWriteTarget = IsWriteTarget(elem);

        if (!IrEvaluationTraits.IsReusableReceiver(receiver) && isWriteTarget)
            return ReportUnsupported(
                elem,
                "IndexFromEndWrite",
                $"Writing through '{elem}' is not supported: the receiver must be a local, "
                    + "parameter, field or property so it can be read twice without side effects."
            );

        var offset = Extract(indexFromEnd.Operand);
        var targetType = MapExpressionType(receiverSyntax);

        IrExpression Access(IrExpression target, IrExpression boundOffset) =>
            new IrElementAccess(target, LengthMinus(target, length, boundOffset), targetType);

        return isWriteTarget
            ? Access(receiver, offset)
            : BindReceiverOnce(
                receiver,
                target => BindValueOnce(offset, boundOffset => Access(target, boundOffset))
            );
    }

    /// <summary>
    /// <c>x[a..b]</c> → <c>x.slice(a, b)</c>, with a from-end bound resolved to
    /// <c>length - n</c>. When the length is read, the receiver and any bound
    /// with side effects are let-bound in source order first, so the length is
    /// read after every bound has been evaluated, as in C#.
    /// </summary>
    private IrExpression ExtractRangeAccess(
        ExpressionSyntax receiverSyntax,
        RangeExpressionSyntax range,
        ISymbol length
    )
    {
        var receiver = Extract(receiverSyntax);
        var targetType = MapExpressionType(receiverSyntax);
        var start = ExtractRangeBound(range.LeftOperand);
        var end = ExtractRangeBound(range.RightOperand);

        if (start is not { FromEnd: true } && end is not { FromEnd: true })
            return new IrSliceExpression(receiver, start?.Value, end?.Value, targetType);

        return BindReceiverOnce(
            receiver,
            target =>
                BindBoundOnce(
                    start,
                    boundStart =>
                        BindBoundOnce(
                            end,
                            boundEnd => new IrSliceExpression(
                                target,
                                ResolveRangeBound(boundStart, target, length),
                                ResolveRangeBound(boundEnd, target, length),
                                targetType
                            )
                        )
                )
        );
    }

    private RangeBound? ExtractRangeBound(ExpressionSyntax? bound) =>
        bound switch
        {
            null => null,
            _ when AsIndexFromEnd(bound) is { } indexFromEnd => new RangeBound(
                Extract(indexFromEnd.Operand),
                FromEnd: true
            ),
            _ => new RangeBound(Extract(bound), FromEnd: false),
        };

    private IrExpression BindBoundOnce(RangeBound? bound, Func<RangeBound?, IrExpression> build) =>
        bound is null
            ? build(null)
            : BindValueOnce(bound.Value, value => build(bound with { Value = value }));

    private IrExpression? ResolveRangeBound(
        RangeBound? bound,
        IrExpression target,
        ISymbol length
    ) =>
        bound switch
        {
            null => null,
            { FromEnd: true } => LengthMinus(target, length, bound.Value),
            _ => bound.Value,
        };

    private IrBinaryExpression LengthMinus(
        IrExpression target,
        ISymbol length,
        IrExpression offset
    ) =>
        new(
            new IrMemberAccess(target, length.Name, BuildOrigin(length)),
            IrBinaryOp.Subtract,
            offset
        );

    /// <summary>
    /// The C# member a from-end offset is measured against: <c>Array.Length</c>
    /// for an array, or the <c>Length</c> / <c>Count</c> member Roslyn bound
    /// for an implicit index/range indexer (string, list). <c>null</c> for a
    /// user-defined indexer that takes the index or range value itself.
    /// </summary>
    private ISymbol? LengthMemberOf(IOperation? access) =>
        access switch
        {
            IArrayElementReferenceOperation => _semantic
                .Compilation.GetSpecialType(SpecialType.System_Array)
                .GetMembers(nameof(Array.Length))
                .FirstOrDefault(),
            IImplicitIndexerReferenceOperation implicitIndexer => implicitIndexer.LengthSymbol,
            _ => null,
        };

    private bool IsLowerableRange(IOperation? access, RangeExpressionSyntax range) =>
        IsSliceableRangeAccess(access) && HasSupportedRangeBounds(range);

    /// <summary>
    /// True when a range access copies a sub-sequence the backends know how to
    /// slice: an array, <c>string.Substring</c>, or <c>List&lt;T&gt;.Slice</c>.
    /// A user type's own <c>Slice</c> method is excluded — its argument
    /// convention (start, length) is not the target's slice convention.
    /// </summary>
    private bool IsSliceableRangeAccess(IOperation? access) =>
        access switch
        {
            IArrayElementReferenceOperation => true,
            IImplicitIndexerReferenceOperation { IndexerSymbol: IMethodSymbol slice } =>
                IsStringSubstring(slice) || IsListSlice(slice),
            _ => false,
        };

    private static bool IsStringSubstring(IMethodSymbol method) =>
        method.ContainingType.SpecialType == SpecialType.System_String
        && method.Name == nameof(string.Substring);

    private bool IsListSlice(IMethodSymbol method) =>
        method.Name == nameof(List<int>.Slice)
        && SymbolEqualityComparer.Default.Equals(
            method.ContainingType.OriginalDefinition,
            _semantic.Compilation.GetTypeByMetadataName(ListTypeName)
        );

    /// <summary>
    /// A range bound is lowered when it is absent, an inline <c>^n</c>, or a
    /// plain <c>int</c> offset; an <see cref="System.Index"/> value read from
    /// a variable is not.
    /// </summary>
    private bool HasSupportedRangeBounds(RangeExpressionSyntax range) =>
        IsSupportedRangeBound(range.LeftOperand) && IsSupportedRangeBound(range.RightOperand);

    private bool IsSupportedRangeBound(ExpressionSyntax? bound) =>
        bound is null
        || AsIndexFromEnd(bound) is not null
        || _semantic.GetTypeInfo(bound).Type?.SpecialType == SpecialType.System_Int32;

    private bool IsWellKnownType(ITypeSymbol? type, string metadataName) =>
        type is not null
        && SymbolEqualityComparer.Default.Equals(
            type,
            _semantic.Compilation.GetTypeByMetadataName(metadataName)
        );

    private static bool HasIndexFromEndBound(RangeExpressionSyntax range) =>
        AsIndexFromEnd(range.LeftOperand) is not null
        || AsIndexFromEnd(range.RightOperand) is not null;

    /// <summary>
    /// The <c>^n</c> expression <paramref name="expression"/> is, ignoring
    /// parentheses, or <c>null</c> when it is anything else.
    /// </summary>
    private static PrefixUnaryExpressionSyntax? AsIndexFromEnd(ExpressionSyntax? expression) =>
        expression is not null
        && Unparenthesize(expression) is PrefixUnaryExpressionSyntax prefix
        && prefix.IsKind(SyntaxKind.IndexExpression)
            ? prefix
            : null;

    /// <summary>
    /// True when <paramref name="elem"/> is the location being written:
    /// an assignment target, an increment/decrement operand, or a
    /// <c>ref</c>/<c>out</c> argument.
    /// </summary>
    private static bool IsWriteTarget(ExpressionSyntax elem) =>
        elem.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == elem,
            PrefixUnaryExpressionSyntax prefix => IsIncrementOrDecrement(prefix),
            PostfixUnaryExpressionSyntax postfix => IsIncrementOrDecrement(postfix),
            ArgumentSyntax argument => argument.RefKindKeyword.Kind()
                is SyntaxKind.RefKeyword
                    or SyntaxKind.OutKeyword,
            _ => false,
        };

    private static bool IsIncrementOrDecrement(ExpressionSyntax unary) =>
        unary.Kind()
            is SyntaxKind.PreIncrementExpression
                or SyntaxKind.PreDecrementExpression
                or SyntaxKind.PostIncrementExpression
                or SyntaxKind.PostDecrementExpression;

    private static ExpressionSyntax Unparenthesize(ExpressionSyntax expression) =>
        expression is ParenthesizedExpressionSyntax parenthesized
            ? Unparenthesize(parenthesized.Expression)
            : expression;
}
