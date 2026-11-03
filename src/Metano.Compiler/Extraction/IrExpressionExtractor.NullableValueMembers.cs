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
///   <item><c>x.Value</c> → <c>x</c> where the nullable flow state proves
///   <c>x</c> non-null, and otherwise <c>x ?? throw new
///   InvalidOperationException(…)</c>, the exception .NET throws.</item>
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

    private IrExpression ValueOrThrowWhenMissing(
        ExpressionSyntax receiverSyntax,
        IrExpression receiver
    ) =>
        IsProvenNonNull(receiverSyntax)
            ? receiver
            : new IrBinaryExpression(
                receiver,
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

    /// <summary>
    /// <c>t.Equals(u)</c> over a nullable tuple (<c>Nullable&lt;T&gt;.Equals</c>)
    /// retargets to the underlying tuple's own <c>Equals</c>, carrying the
    /// tuple type: C# answers both null <c>true</c>, one null <c>false</c>,
    /// else element-wise — the null-guarded comparison a backend's tuple
    /// equality applies (#331). <c>t.Value.Equals(u)</c> needs no special
    /// case: <see cref="ValueOrThrowWhenMissing"/> already guards the
    /// receiver, so the call lands on the tuple itself. <c>null</c> for any
    /// other call.
    /// </summary>
    private IrExpression? TryExtractNullableTupleEquals(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method
    )
    {
        if (
            method
                is not {
                    Name: nameof(object.Equals),
                    IsStatic: false,
                    ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                    Parameters: [{ Type.SpecialType: SpecialType.System_Object }],
                }
            || method.ContainingType.TypeArguments[0]
                is not INamedTypeSymbol { IsTupleType: true } tuple
            || SymbolHelper.InstanceEqualsOverObject(tuple) is not { } tupleEquals
            || QualifiedMemberSyntax.From(invocation.Expression) is not { } member
            || invocation.ArgumentList.Arguments is not [{ NameColon: null } argument]
        )
        {
            return null;
        }

        return new IrCallExpression(
            new IrMemberAccess(Extract(member.Receiver), tupleEquals.Name),
            [new IrArgument(Extract(argument.Expression))],
            Origin: BuildOrigin(tupleEquals),
            TupleType: IrTypeRefMapper.Map(tuple, _originResolver, _target) as IrTupleTypeRef
        );
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
    /// Whether nullable analysis proves the receiver non-null (a
    /// <c>HasValue</c> or <c>!= null</c> check dominates it). Roslyn's flow
    /// state is optimistic — a call between the check and the read does not
    /// invalidate it — so such a read stays unguarded, as Roslyn's own
    /// warning does. A nullable-disabled context has no flow state; there
    /// the read is left unguarded too.
    /// </summary>
    private bool IsProvenNonNull(ExpressionSyntax receiverSyntax) =>
        _semantic.GetTypeInfo(receiverSyntax).Nullability.FlowState != NullableFlowState.MaybeNull;

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
