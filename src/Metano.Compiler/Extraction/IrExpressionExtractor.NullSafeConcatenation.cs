using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// C# string concatenation (<c>+</c>, <c>+=</c>) and interpolation format a
/// <c>null</c> operand as the empty string, where JavaScript writes
/// <c>"null"</c> (<c>"n" + null</c> is <c>"nnull"</c>). An operand that may
/// be <c>null</c> — a nullable value, a <c>null</c> literal, or a reference
/// or type parameter that is not <see cref="ReferenceNullability">null by
/// construction</see> — is therefore extracted as <c>operand ?? ""</c>,
/// whatever the (optimistic, or absent when nullable is disabled) flow state
/// says.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private static readonly IrLiteral EmptyStringLiteral = new("", IrLiteralKind.String);

    /// <summary>
    /// <c>left + right</c> when it is a string concatenation, with each
    /// operand that may be <c>null</c> formatted as <c>""</c>
    /// (<c>left + (right ?? "")</c>); <c>null</c> for any other operator.
    /// </summary>
    private IrExpression? TryExtractStringConcatenation(BinaryExpressionSyntax bin)
    {
        if (!IsStringConcatenation(bin))
            return null;

        return new IrBinaryExpression(
            BoxTextOperand(bin.Left, ConcatenationOperand(bin.Left)),
            IrBinaryOp.Add,
            BoxTextOperand(bin.Right, ConcatenationOperand(bin.Right))
        );
    }

    /// <summary>
    /// <c>target += value</c> on a string: a <c>value</c> that may be
    /// <c>null</c> is appended as <c>""</c>, and so is a <c>target</c> that
    /// may be <c>null</c> (<c>target = (target ?? "") + value</c>).
    /// The existing read-modify-write binding evaluates its receiver and index once.
    /// </summary>
    private IrExpression? TryExtractStringCompoundConcatenation(
        AssignmentExpressionSyntax assign,
        IrExpression target
    )
    {
        if (!IsStringConcatenation(assign))
            return null;

        var value = BoxTextOperand(assign.Right, ConcatenationOperand(assign.Right));

        if (!MayFormatAsNull(assign.Left))
            return new IrBinaryExpression(target, IrBinaryOp.AddAssign, value);

        return BuildReadModifyWrite(target, assign, current => new IrBinaryExpression(
            new IrBinaryExpression(current, IrBinaryOp.NullCoalescing, EmptyStringLiteral),
            IrBinaryOp.Add,
            value
        ));
    }

    /// <summary>
    /// An interpolation hole formats a <c>null</c> value as <c>""</c>
    /// whatever its format or alignment clause, so a hole that may be
    /// <c>null</c> reads <c>value ?? ""</c>.
    /// </summary>
    private IrExpression ExtractInterpolationHole(InterpolationSyntax hole) =>
        ConcatenationOperand(hole.Expression);

    private bool IsStringConcatenation(ExpressionSyntax operatorSyntax) =>
        _semantic.GetSymbolInfo(operatorSyntax).Symbol
            is IMethodSymbol
            {
                MethodKind: MethodKind.BuiltinOperator,
                Name: WellKnownMemberNames.AdditionOperatorName,
                ContainingType.SpecialType: SpecialType.System_String,
            };

    private IrExpression ConcatenationOperand(ExpressionSyntax operand)
    {
        var value = Extract(operand);

        if (DefaultTextCall(operand, value) is { } text)
            return text;

        // A base-object member call lowers to `object`'s defaults (#506), and
        // none of them is null — `object.ToString`'s `string?` annotation
        // notwithstanding — so the coalesce would be unreachable code tsgo
        // rejects (TS2869) over a string literal.
        if (!MayFormatAsNull(operand) || value is IrBaseObjectMemberCall)
            return value;

        return new IrBinaryExpression(
            WithoutNonNullAssertion(value),
            IrBinaryOp.NullCoalescing,
            EmptyStringLiteral
        );
    }

    /// <summary>
    /// <c>s!</c> asserts a value the guard then tests, so the assertion is
    /// dropped under it: <c>s ?? ""</c>, not <c>s! ?? ""</c>.
    /// </summary>
    private static IrExpression WithoutNonNullAssertion(IrExpression value) =>
        value is IrUnaryExpression { Operator: IrUnaryOp.NonNullAssertion, Operand: var inner }
            ? WithoutNonNullAssertion(inner)
            : value;

    /// <summary>
    /// <c>value.ToString()</c> for an operand whose static type is a <c>bool</c>,
    /// <c>double</c> or <c>float</c> (or its nullable form) when the target maps that call
    /// (<see cref="DefaultTextConversion"/>, #308): the mapping prints .NET's text and a
    /// <c>null</c> as <c>""</c>, so the operand needs no coalesce. <c>null</c> for any other
    /// operand.
    /// </summary>
    private IrCallExpression? DefaultTextCall(ExpressionSyntax operand, IrExpression value)
    {
        var type = _semantic.GetTypeInfo(operand).Type;

        if (type?.UnwrapNullableValueType().HasStringEnum() == true)
            return null;

        return
            DefaultTextConversion.MappedToString(
                type is ITypeParameterSymbol ? _semantic.Compilation.ObjectType : type,
                _context.LowersByDeclarativeMapping
            )
                is { } toString
            ? DefaultTextCall(value, toString, type)
            : null;
    }

    private IrCallExpression DefaultTextCall(
        IrExpression value,
        IMethodSymbol toString,
        ITypeSymbol? type
    ) =>
        DefaultTextConversion.Call(value, toString, BuildOrigin(toString)) with
        {
            TypeArguments = type?.UnwrapNullableValueType() is { TypeKind: TypeKind.Enum } enumType
                ? [IrTypeRefMapper.Map(enumType, _originResolver, _target)]
                : null,
        };

    private IrExpression? TryExtractDefaultTextInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method
    )
    {
        if (
            method is not { Name: nameof(ToString), IsStatic: false, Parameters.Length: 0 }
            || !method.ContainingAssembly.IsBclAssembly()
            || QualifiedMemberSyntax.From(invocation.Expression) is not { } member
        )
            return null;

        var type = _semantic.GetTypeInfo(member.Receiver).Type;

        if (
            DefaultTextConversion.MappedToString(
                type is ITypeParameterSymbol ? _semantic.Compilation.ObjectType : type,
                _context.LowersByDeclarativeMapping
            ) is
            { } toString
        )
        {
            var value = Extract(member.Receiver);
            var text = DefaultTextCall(value, toString, type);

            return toString.ContainingType.SpecialType == SpecialType.System_Object
                ? text with
                {
                    Target = new IrMemberAccess(value, toString.Name, BuildOrigin(toString)),
                }
                : text;
        }

        return null;
    }

    private bool MayFormatAsNull(ExpressionSyntax operand) =>
        ReferenceNullability.MayBeNull(_semantic, operand);
}
