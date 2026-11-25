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

        var value = StringCompoundOperand(assign);

        if (!MayFormatAsNull(assign.Left))
            return new IrBinaryExpression(target, IrBinaryOp.AddAssign, value);

        return BuildReadModifyWrite(target, assign, current =>
            StringCompoundValue(assign, current, value)
        );
    }

    /// <summary>
    /// The right-hand side of a string <c>target += value</c>, appended as
    /// <c>""</c> when it may be <c>null</c>.
    /// </summary>
    private IrExpression StringCompoundOperand(AssignmentExpressionSyntax assign) =>
        BoxTextOperand(assign.Right, ConcatenationOperand(assign.Right));

    /// <summary>
    /// The value a string <c>target += value</c> stores over the target's
    /// <paramref name="current"/> value: <c>(current ?? "") + value</c> when
    /// the target may be <c>null</c>, <c>current + value</c> otherwise.
    /// </summary>
    private IrExpression StringCompoundValue(
        AssignmentExpressionSyntax assign,
        IrExpression current,
        IrExpression value
    ) =>
        new IrBinaryExpression(
            MayFormatAsNull(assign.Left)
                ? new IrBinaryExpression(current, IrBinaryOp.NullCoalescing, EmptyStringLiteral)
                : current,
            IrBinaryOp.Add,
            value
        );

    /// <summary>
    /// An interpolation hole formats a <c>null</c> value as <c>""</c>
    /// whatever its format or alignment clause, so a hole that may be
    /// <c>null</c> reads <c>value ?? ""</c>.
    /// </summary>
    private IrExpression ExtractInterpolationHole(InterpolationSyntax hole) =>
        ConcatenationOperand(hole.Expression);

    private bool IsStringConcatenation(ExpressionSyntax operatorSyntax) =>
        DefaultTextSites.Classify(_semantic.GetOperation(operatorSyntax))
        == DefaultTextSite.Implicit;

    private IrExpression ConcatenationOperand(ExpressionSyntax operand)
    {
        var type = _semantic.GetTypeInfo(operand).Type;

        ReportUnprintableText(operand, type);

        return DefaultText(
            Extract(operand),
            type,
            DefaultTextSites.ForOperand(operand, _semantic),
            MayFormatAsNull(operand)
        );
    }

    /// <summary>
    /// The default text of the record member <paramref name="member"/> read
    /// through <c>this</c>: exactly what the interpolation hole
    /// <c>$"{this.Member}"</c> extracts to, so a record's synthesized text
    /// (baseline FR-093) and a source hole share one lowering.
    /// </summary>
    internal IrExpression ExtractMemberDefaultText(ISymbol member, ITypeSymbol memberType) =>
        DefaultText(
            new IrMemberAccess(new IrThisExpression(), member.Name, BuildOrigin(member)),
            memberType,
            DefaultTextSite.Implicit,
            ReferenceNullability.MayBeNull(memberType)
        );

    /// <summary>
    /// <paramref name="value"/> as text at a default-text site: the mapped
    /// <c>ToString()</c> of its type (<see cref="DefaultTextConversion"/>), else the
    /// value itself, coalesced to <c>""</c> when <paramref name="mayBeNull"/>.
    /// </summary>
    private IrExpression DefaultText(
        IrExpression value,
        ITypeSymbol? type,
        DefaultTextSite site,
        bool mayBeNull
    )
    {
        if (DefaultTextCall(value, type, site) is { } text)
            return text;

        // A base-object member call lowers to `object`'s defaults (#506), and
        // none of them is null — `object.ToString`'s `string?` annotation
        // notwithstanding — so the coalesce would be unreachable code tsgo
        // rejects (TS2869) over a string literal.
        if (!mayBeNull || value is IrBaseObjectMemberCall)
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
    private IrCallExpression? DefaultTextCall(
        IrExpression value,
        ITypeSymbol? type,
        DefaultTextSite site
    ) =>
        DefaultTextConversion.MappedToString(
            type is ITypeParameterSymbol ? _semantic.Compilation.ObjectType : type,
            new(_target, _context.LowersByDeclarativeMapping, site)
        )
            is { } toString
            ? DefaultTextCall(value, toString, type)
            : null;

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
        ReportUnprintableToString(invocation, method);

        if (
            !DefaultTextSites.IsInvocation(method)
            || QualifiedMemberSyntax.From(invocation.Expression) is not { } member
        )
            return null;

        var type = _semantic.GetTypeInfo(member.Receiver).Type;

        if (
            DefaultTextConversion.MappedToString(
                type is ITypeParameterSymbol ? _semantic.Compilation.ObjectType : type,
                new(_target, _context.LowersByDeclarativeMapping, DefaultTextSite.Invocation)
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
