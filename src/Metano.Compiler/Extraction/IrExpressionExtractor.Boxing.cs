using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The boxing facts of spec 011 FR-050 / FR-051, as Roslyn reports them: a
/// boxing conversion of an enum or <c>[InlineWrapper]</c> value to
/// <c>object</c>, <c>ValueType</c> or <c>Enum</c> becomes an
/// <see cref="IrBoxExpression"/>, and a value whose static type can hold a box
/// crossing an interop boundary becomes an <see cref="IrUnboxExpression"/>.
/// An interop-bound slot (an argument of an <c>[Import]</c> / <c>[Emit]</c>
/// member, a <c>[PlainObject]</c> or <c>[JsTuple]</c> member) gets no box: JS
/// reads it directly. Whether and how a target boxes is its own decision; a
/// target that keeps the reflection source forms (Dart) gets neither node.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <paramref name="value"/> boxed when Roslyn reports a boxing conversion of
    /// an enum or wrapper for <paramref name="expression"/>, or unwrapped when it
    /// is an argument of an interop member; <paramref name="value"/> otherwise.
    /// A parenthesized, <c>checked</c> / <c>unchecked</c> or null-forgiving
    /// expression leaves the conversion to its operand.
    /// </summary>
    private IrExpression WithBoxingConversion(ExpressionSyntax expression, IrExpression value)
    {
        if (
            _context.KeepsReflectionSourceForms
            || expression is ParenthesizedExpressionSyntax or CheckedExpressionSyntax
            || expression.IsKind(SyntaxKind.SuppressNullableWarningExpression)
        )
            return value;

        var typeInfo = _semantic.GetTypeInfo(expression);
        var conversion = _semantic.GetConversion(expression);

        var boxed =
            conversion is { IsBoxing: true, IsImplicit: true }
            && BoxOf(value, typeInfo.Type, typeInfo.ConvertedType) is { } box
                ? box
                : value;

        return BoxingSlotOf(expression) switch
        {
            BoxingSlot.Unboxed => InteropUnboxed(typeInfo.Type, value),
            BoxingSlot.Raw => WithoutBox(value),
            BoxingSlot.Text when BoxingSlots.CanHoldBox(typeInfo.ConvertedType) => new IrTextValue(
                boxed
            ),
            _ => boxed,
        };
    }

    /// <summary>
    /// The slot <paramref name="expression"/> fills (<see cref="BoxingSlots"/>,
    /// the one classification the runtime-question scan shares); only an
    /// argument, an initializer element, a collection-expression element or an
    /// assigned value can fill anything but an ordinary slot.
    /// </summary>
    private BoxingSlot BoxingSlotOf(ExpressionSyntax expression)
    {
        SyntaxNode filled = expression;

        while (
            filled.Parent is ParenthesizedExpressionSyntax or CheckedExpressionSyntax
            || filled.Parent?.IsKind(SyntaxKind.SuppressNullableWarningExpression) is true
        )
            filled = filled.Parent;

        var mayFillASlot = filled.Parent switch
        {
            ArgumentSyntax or InitializerExpressionSyntax or ExpressionElementSyntax => true,
            AssignmentExpressionSyntax assignment => assignment.Right == filled,
            _ => false,
        };

        return mayFillASlot && _semantic.GetOperation(expression) is { } operation
            ? BoxingSlots.SlotOf(
                operation,
                _semantic.Compilation.Assembly,
                LowersByDeclarativeMapping
            )
            : BoxingSlot.Boxed;
    }

    /// <summary>
    /// The box of <paramref name="value"/> for a boxing conversion from
    /// <paramref name="source"/> to <paramref name="target"/>; <c>null</c> when the
    /// source is not an enum or wrapper (a nullable of one included) or the target
    /// is not <c>object</c>, <c>ValueType</c> or <c>Enum</c> (an interface stays raw).
    /// </summary>
    private IrBoxExpression? BoxOf(IrExpression value, ITypeSymbol? source, ITypeSymbol? target)
    {
        if (
            source is null
            || target?.SpecialType
                is not (
                    SpecialType.System_Object
                    or SpecialType.System_ValueType
                    or SpecialType.System_Enum
                )
        )
            return null;

        var boxed = source.UnwrapNullableValueType();

        if (boxed.TypeKind is not TypeKind.Enum && !SymbolHelper.HasInlineWrapper(boxed))
            return null;

        return new IrBoxExpression(
            value,
            IrTypeRefMapper.Map(boxed, _originResolver, _target),
            IsLifted: source.IsNullableValueType()
        );
    }

    /// <summary>
    /// The box of an explicit boxing cast (<c>(object)Color.Red</c>); <c>null</c>
    /// for any other cast.
    /// </summary>
    private IrBoxExpression? ExplicitBoxOf(
        CastExpressionSyntax cast,
        IrExpression inner,
        ITypeSymbol sourceType,
        ITypeSymbol targetType
    ) =>
        !_context.KeepsReflectionSourceForms
        && BoxingSlotOf(cast) is BoxingSlot.Boxed or BoxingSlot.Text
        && ConversionOfCast(cast, targetType).IsBoxing
            ? BoxOf(inner, sourceType, targetType)
            : null;

    /// <summary>
    /// <paramref name="value"/> unwrapped where it crosses into code that reads
    /// it raw (FR-051): no box is created there, and a value whose static type
    /// can hold one (<c>object</c>, <c>ValueType</c>, <c>Enum</c>, <c>dynamic</c>,
    /// a type parameter) is unboxed. Any other value is unchanged.
    /// </summary>
    private IrExpression InteropUnboxed(ITypeSymbol? staticType, IrExpression value)
    {
        if (_context.KeepsReflectionSourceForms)
            return value;

        var raw = WithoutBox(value);

        return BoxingSlots.CanHoldBox(staticType)
            ? new IrUnboxExpression(
                raw,
                IrTypeRefMapper.Map(staticType!, _originResolver, _target),
                Interop: true
            )
            : raw;
    }

    /// <summary>
    /// A string-concatenation operand that may hold a box, marked so a target
    /// appends its text (FR-051); any other operand unchanged.
    /// </summary>
    private IrExpression BoxTextOperand(ExpressionSyntax operand, IrExpression value)
    {
        var type = _semantic.GetTypeInfo(operand).Type;

        return
            !_context.KeepsReflectionSourceForms
            && (BoxingSlots.CanHoldBox(type) || type?.TypeKind is TypeKind.Interface)
            ? new IrTextValue(value)
            : value;
    }

    /// <summary><paramref name="value"/> without the box its own conversion created.</summary>
    private static IrExpression WithoutBox(IrExpression value) =>
        value is IrBoxExpression box ? box.Value : value;

    /// <summary>
    /// <paramref name="pattern"/> with the types a target needs to compare a
    /// value that may hold the enum constant only as a box (<c>o is Color.Red</c>
    /// on an <c>object</c>, FR-050); unchanged for any other constant.
    /// </summary>
    private IrConstantPattern WithBoxedConstantFacts(
        IrConstantPattern pattern,
        ExpressionSyntax constant,
        ITypeSymbol? inputType
    ) =>
        BoxedConstantFactsOf(constant, inputType) is var (input, type)
            ? pattern with
            {
                InputType = input,
                ConstantType = type,
            }
            : pattern;

    /// <summary>
    /// The constant of <c>x is Color.Red</c>, which C# parses as a type name:
    /// a static field read through its type; any other constant extracts as
    /// an expression.
    /// </summary>
    private IrExpression ExtractConstantName(ExpressionSyntax constant) =>
        constant is QualifiedNameSyntax
        && _semantic.GetSymbolInfo(constant).Symbol is IFieldSymbol { IsStatic: true } field
            ? new IrMemberAccess(
                TypeReferenceTo(field.ContainingType),
                field.Name,
                BuildOrigin(field)
            )
            : Extract(constant);

    private IrConstantCaseLabel WithBoxedConstantFacts(CaseSwitchLabelSyntax label)
    {
        var lowered = new IrConstantCaseLabel(Extract(label.Value));

        return BoxedConstantFactsOf(label.Value, SwitchSubjectType(label)) is var (input, type)
            ? lowered with
            {
                InputType = input,
                ConstantType = type,
            }
            : lowered;
    }

    private (IrTypeRef Input, IrTypeRef Constant)? BoxedConstantFactsOf(
        ExpressionSyntax constant,
        ITypeSymbol? inputType
    )
    {
        var constantType = _semantic.GetTypeInfo(constant).Type;

        if (
            _context.KeepsReflectionSourceForms
            || constantType is not { TypeKind: TypeKind.Enum }
            || inputType is null
            || !(BoxingSlots.CanHoldBox(inputType) || inputType.TypeKind is TypeKind.Interface)
            || inputType is IDynamicTypeSymbol
        )
            return null;

        return (
            IrTypeRefMapper.Map(inputType, _originResolver, _target),
            IrTypeRefMapper.Map(constantType, _originResolver, _target)
        );
    }

    /// <summary>
    /// <c>o.Equals(x)</c> bound to <c>object.Equals(object)</c> on a receiver
    /// whose static type is <c>object</c>, <c>ValueType</c> or <c>Enum</c>, or
    /// an enum value's <c>Equals(object)</c> (FR-051): the runtime value's
    /// equality answers, a box comparing its type and value, so the argument is
    /// boxed as C# boxes it, and so is an enum receiver. <c>null</c> for any
    /// other call.
    /// </summary>
    private IrExpression? TryExtractDefaultEquals(
        InvocationExpressionSyntax inv,
        IMethodSymbol? method
    )
    {
        if (
            _context.KeepsReflectionSourceForms
            || method is not { Name: "Equals", IsStatic: false, Parameters.Length: 1 }
            || QualifiedMemberSyntax.From(inv.Expression) is not { } access
            || access.Receiver is BaseExpressionSyntax
            || inv.ArgumentList.Arguments is not [{ Expression: var argument }]
            || DefaultEqualsReceiver(access.Receiver, method) is not { } receiver
        )
            return null;

        return new IrBinaryExpression(receiver, IrBinaryOp.DefaultEqual, Extract(argument));
    }

    /// <summary>
    /// The left operand of a default <c>Equals</c>: a receiver that may hold a
    /// box as it is, an enum receiver boxed; <c>null</c> for any other receiver
    /// (a type parameter or <c>dynamic</c> one included).
    /// </summary>
    private IrExpression? DefaultEqualsReceiver(ExpressionSyntax receiver, IMethodSymbol method)
    {
        var type = _semantic.GetTypeInfo(receiver).Type;

        if (BoxingSlots.IsEnumEquals(method, type))
            return BoxOf(Extract(receiver), type, _semantic.Compilation.ObjectType);

        return
            method.ContainingType.SpecialType is SpecialType.System_Object
            && BoxingSlots.CanHoldBox(type)
            && type is not (ITypeParameterSymbol or IDynamicTypeSymbol)
            ? Extract(receiver)
            : null;
    }
}
