using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The members of <c>Nullable&lt;T&gt;</c>. A nullable value is the value
/// itself or <c>null</c> at runtime in every target, so the members lower to
/// null tests over the receiver instead of member reads that do not exist:
/// <list type="bullet">
///   <item><c>x.HasValue</c> → <c>x != null</c>.</item>
///   <item><c>x.Value</c>, and an explicit conversion to the non-nullable
///   type (<c>(int)x</c>) → <c>x ?? throw new
///   InvalidOperationException(…)</c>, the exception .NET throws. The
///   throw is left out only where the operand is, through parentheses, a
///   cast to a nullable value type of a non-nullable value
///   (<c>((int?)5).Value</c>, <c>(int)(int?)x</c>): that value is present
///   by construction. Nullable analysis does not remove it (#629): its flow
///   state is optimistic (after <c>x!</c>, for a local a lambda clears, for
///   a property checked and then cleared by a call) and absent in
///   nullable-disabled code, so a read it calls safe can still be missing.
///   Dart is the exception: its analyzer rejects a guard over a promoted
///   local as dead code, so there the flow state still decides.</item>
///   <item><c>x.GetValueOrDefault()</c> → <c>x ?? default(T)</c>, and
///   <c>x.GetValueOrDefault(d)</c> → <c>x ?? d</c>.</item>
/// </list>
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string NullableValueMissingMessage = "Nullable object must have a value.";

    private IrExpression? TryExtractNullableValueMember(
        QualifiedMemberSyntax member,
        IrExpression receiver
    )
    {
        var name = member.Name.Identifier.ValueText;

        if (name is not ("HasValue" or "Value") || !IsNullableValueMember(member))
            return null;

        return name == "HasValue"
            ? IsNotNull(receiver)
            : ValueOrThrowWhenMissing(member.Receiver, receiver);
    }

    /// <summary>
    /// Whether the member resolves to a <c>Nullable&lt;T&gt;</c> property,
    /// by symbol: a <c>Value</c> / <c>HasValue</c> of the underlying struct
    /// (<c>money?.Value</c>, read through a null-conditional access) is an
    /// ordinary member read.
    /// </summary>
    private bool IsNullableValueMember(QualifiedMemberSyntax member) =>
        _semantic.GetSymbolInfo(member.Node).Symbol
            is IPropertySymbol
            {
                ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            };

    /// <summary>
    /// An explicit conversion from a nullable value to a non-nullable value
    /// type (<c>(int)n</c>, <c>(long)n</c>), which throws like
    /// <c>n.Value</c> when the value is missing.
    /// </summary>
    private static bool UnwrapsNullableValue(ITypeSymbol sourceType, ITypeSymbol targetType) =>
        sourceType.IsNullableValueType()
        && targetType.IsValueType
        && !targetType.IsNullableValueType();

    /// <summary>
    /// The value of a <c>Nullable&lt;T&gt;</c> read or unwrap cast: guarded
    /// by the .NET throw, unless the target reads
    /// <paramref name="receiverSyntax"/> without one.
    /// </summary>
    private IrExpression ValueOrThrowWhenMissing(
        ExpressionSyntax receiverSyntax,
        IrExpression receiver
    ) => IsReadWithoutAGuard(receiverSyntax) ? receiver : GuardedValueOrThrow(receiver);

    /// <summary>
    /// Whether the target reads the value of <paramref name="receiverSyntax"/>
    /// with no missing-value throw. TypeScript only where the syntax wraps a
    /// present value (<see cref="WrapsAPresentValue"/>), in every nullable
    /// context. Dart wherever nullable analysis does not report the receiver
    /// as maybe-null: its analyzer rejects a guard over a promoted local
    /// (<c>dead_code</c>, <c>dead_null_aware_expression</c>).
    /// </summary>
    private bool IsReadWithoutAGuard(ExpressionSyntax receiverSyntax) =>
        // TODO(#549): decide by a target capability instead of the Dart special case.
        IsDartTarget
            ? _semantic.GetTypeInfo(receiverSyntax).Nullability.FlowState
                != NullableFlowState.MaybeNull
            : WrapsAPresentValue(receiverSyntax);

    /// <summary>
    /// Whether <paramref name="expression"/> is, through any parentheses, a
    /// built-in cast to a nullable value type of a non-nullable value
    /// (<c>(int?)2</c>, <c>(long?)x</c> over an <c>int</c>): the nullable it
    /// yields always has a value. Decided from the syntax and the declared
    /// types alone, never from nullable flow analysis. A user-defined
    /// conversion operator is not one: it returns whatever its body does.
    /// </summary>
    private bool WrapsAPresentValue(ExpressionSyntax expression) =>
        expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => WrapsAPresentValue(
                parenthesized.Expression
            ),
            CastExpressionSyntax cast => _semantic.GetTypeInfo(cast).Type.IsNullableValueType()
                && _semantic.GetTypeInfo(cast.Expression).Type is { IsValueType: true } operand
                && !operand.IsNullableValueType()
                && _semantic.GetSymbolInfo(cast).Symbol is null,
            _ => false,
        };

    /// <summary>
    /// The value itself, or the .NET throw when it is missing, always
    /// guarded: the runtime shape of an explicit nullable unwrap whose value
    /// is not present by construction — a <c>Value</c> read, an unwrap cast,
    /// a converted tuple, and a <c>foreach</c> element read into a
    /// non-nullable loop variable (issue #488).
    /// </summary>
    internal IrExpression GuardedValueOrThrow(IrExpression value) =>
        new IrBinaryExpression(
            value,
            IrBinaryOp.NullCoalescing,
            new IrThrowExpression(BuildNullableValueMissingException())
        );

    /// <summary>
    /// <c>x.GetValueOrDefault()</c> / <c>x.GetValueOrDefault(d)</c> on a
    /// <c>Nullable&lt;T&gt;</c>, or <c>null</c> for any other call. A
    /// side-effecting <c>d</c> is evaluated even when <c>x</c> has a value,
    /// as the C# argument is, so both are evaluated once, in source order.
    /// The default of a type parameter <c>T</c> has no runtime value, so
    /// <c>GetValueOrDefault()</c> on a <c>T?</c> reports MS0001.
    /// </summary>
    private IrExpression? TryExtractNullableGetValueOrDefault(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method
    )
    {
        if (
            !IsNullableGetValueOrDefault(method)
            || QualifiedMemberSyntax.From(invocation.Expression) is not { } member
        )
            return null;

        var receiver = Extract(member.Receiver);

        return invocation.ArgumentList.Arguments is [var argument]
            ? ValueOrArgument(receiver, Extract(argument.Expression))
            : ValueOrTypeDefault(invocation, receiver, method!.ContainingType.TypeArguments[0]);
    }

    private static bool IsNullableGetValueOrDefault(IMethodSymbol? method) =>
        method
            is {
                Name: "GetValueOrDefault",
                ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            };

    private IrExpression ValueOrTypeDefault(
        InvocationExpressionSyntax invocation,
        IrExpression receiver,
        ITypeSymbol valueType
    )
    {
        if (valueType is ITypeParameterSymbol)
            return ReportUnsupported(
                invocation,
                "NullableGetValueOrDefault",
                $"'{invocation}' cannot be lowered: default({valueType.Name}) of a type "
                    + "parameter has no runtime value; pass the default explicitly."
            );

        return new IrBinaryExpression(
            receiver,
            IrBinaryOp.NullCoalescing,
            DefaultValueLiterals.For(valueType, BuildOrigin)
        );
    }

    private IrExpression ValueOrArgument(IrExpression receiver, IrExpression defaultValue)
    {
        if (IrEvaluationTraits.IsSideEffectFree(defaultValue))
            return new IrBinaryExpression(receiver, IrBinaryOp.NullCoalescing, defaultValue);

        // The receiver is read before the default's side effects run, so only
        // a literal receiver can skip its binding.
        return BindOnce(
            receiver,
            isReusable: receiver is IrLiteral,
            boundReceiver =>
                BindOnce(
                    defaultValue,
                    isReusable: false,
                    boundDefault => new IrBinaryExpression(
                        boundReceiver,
                        IrBinaryOp.NullCoalescing,
                        boundDefault
                    )
                )
        );
    }

    /// <summary>
    /// <c>new InvalidOperationException("Nullable object must have a value.")</c>,
    /// classified like any BCL exception construction so each backend lowers
    /// it to its native error.
    /// </summary>
    private IrExpression BuildNullableValueMissingException()
    {
        var exceptionType = _semantic.Compilation.GetTypeByMetadataName(
            "System.InvalidOperationException"
        )!;

        var constructor = exceptionType.InstanceConstructors.Single(c =>
            c.Parameters is [{ Type.SpecialType: SpecialType.System_String }]
        );

        IReadOnlyList<IrArgument> arguments =
        [
            new IrArgument(new IrLiteral(NullableValueMissingMessage, IrLiteralKind.String)),
        ];

        return new IrNewExpression(
            IrTypeRefMapper.Map(exceptionType, _originResolver, _target),
            arguments,
            ExceptionConstruction: IrExceptionConstructionExtractor.Extract(
                constructor,
                arguments,
                exceptionType,
                _target
            )
        );
    }
}
