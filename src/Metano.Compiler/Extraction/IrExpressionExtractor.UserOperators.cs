using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The applications of user-defined operators and conversions (baseline
/// FR-077, FR-079): each one C# binds to an <c>operator</c> method of a
/// transpiled class or struct becomes a neutral
/// <see cref="IrUserOperatorApplication"/>, <see cref="IrUserOperatorStore"/>
/// or <see cref="IrUserConversion"/> carrying the method's facts
/// (<see cref="IrUserOperator"/>) and Roslyn's bound operands. Each target
/// decides the shape. An operator of a BCL type (<c>DateTime</c>,
/// <c>BigInteger</c>), of an interface (generic math), of an
/// <c>[External]</c> or <c>[PlainObject]</c> type, or of a
/// <c>[JsxComponentBuilder]</c> family keeps its existing lowering.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <paramref name="method"/> when it is a user-defined operator or
    /// conversion the extraction applies: declared by a transpiled class or
    /// struct, not synthesized (a record's <c>==</c> keeps its value lowering).
    /// </summary>
    private IMethodSymbol? AppliedUserOperator(IMethodSymbol? method) =>
        method
            is {
                MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion,
                IsImplicitlyDeclared: false,
                IsStatic: true,
                ContainingType.TypeKind: TypeKind.Class or TypeKind.Struct,
            }
        && EmittedTypes.IsEmittedContainingType(method.ContainingType.OriginalDefinition, _target)
        && !IsJsxComponentFamily(method.ContainingType)
            ? method
            : null;

    /// <summary>
    /// A <c>[JsxComponentBuilder]</c> family: its conversion to its element
    /// type is the JSX render protocol, which the core extracts as the JSX
    /// element itself rather than as an operator call.
    /// </summary>
    private static bool IsJsxComponentFamily(INamedTypeSymbol type) =>
        type.HasJsxComponentBuilder() || type.DerivesFromJsxComponentBuilder();

    /// <summary>
    /// The <c>MS0001</c> placeholder of an application whose operator the
    /// targets cannot call (baseline FR-077): a C# 14 instance compound or
    /// increment operator (<c>void operator +=(int k)</c>), or an operator of
    /// a <c>[PlainObject]</c> type, which has no class to declare a helper
    /// on; <c>null</c> for any other operator, and for a target that keeps
    /// these shapes as the source wrote them
    /// (<see cref="IrExtractionContext.KeepsUserOperatorSourceForms"/>).
    /// </summary>
    private IrExpression? UnsupportedUserOperator(IMethodSymbol? method, SyntaxNode node)
    {
        if (_context.KeepsUserOperatorSourceForms)
            return null;

        if (
            method
            is not {
                MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion,
                IsImplicitlyDeclared: false,
            }
        )
            return null;

        if (!method.IsStatic)
        {
            return ReportUnsupported(
                node,
                node.Kind().ToString(),
                $"'{node}' uses the instance operator '{method.Name}', which is not supported "
                    + "(baseline FR-077)."
            );
        }

        return method.ContainingType.HasPlainObject()
            ? ReportUnsupported(
                node,
                node.Kind().ToString(),
                $"'{node}' uses an operator of the [PlainObject] type "
                    + $"'{method.ContainingType.Name}', which has no class to declare it on "
                    + "(baseline FR-077)."
            )
            : null;
    }

    /// <summary>
    /// A condition (of <c>if</c>, <c>while</c>, <c>do</c>, <c>for</c> or
    /// <c>?:</c>) that C# tests through a user-defined <c>true</c> operator,
    /// which is not supported (<c>MS0001</c>, baseline FR-077); <c>null</c>
    /// for any other expression, and for a target that keeps it as the
    /// source wrote it (<see cref="IrExtractionContext.KeepsUserOperatorSourceForms"/>).
    /// A condition over <c>implicit operator bool</c> is a conversion (FR-079).
    /// </summary>
    private IrExpression? UnsupportedUserTruthTest(ExpressionSyntax expression)
    {
        if (_context.KeepsUserOperatorSourceForms)
            return null;

        var isCondition = expression.Parent switch
        {
            IfStatementSyntax statement => statement.Condition == expression,
            WhileStatementSyntax statement => statement.Condition == expression,
            DoStatementSyntax statement => statement.Condition == expression,
            ForStatementSyntax statement => statement.Condition == expression,
            ConditionalExpressionSyntax conditional => conditional.Condition == expression,
            _ => false,
        };

        if (
            !isCondition
            || _semantic.GetTypeInfo(expression).ConvertedType?.SpecialType
                is SpecialType.System_Boolean
        )
            return null;

        var condition = _semantic.GetOperation(expression.Parent!) switch
        {
            IConditionalOperation conditional => conditional.Condition,
            IWhileLoopOperation loop => loop.Condition,
            IForLoopOperation loop => loop.Condition,
            _ => null,
        };

        return condition is IUnaryOperation { OperatorMethod: { } truth }
            ? ReportUnsupported(
                expression,
                expression.Kind().ToString(),
                $"'{expression}' is tested through the user-defined operator '{truth.Name}', "
                    + "which is not supported (baseline FR-077)."
            )
            : null;
    }

    /// <summary>The neutral facts of <paramref name="method"/> (<see cref="IrUserOperator"/>).</summary>
    private IrUserOperator UserOperatorOf(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;

        var overload =
            definition.MethodKind == MethodKind.UserDefinedOperator
                ? DispatchedMethodCalls.RefOf(method, _context)
                : null;

        var pin =
            definition.MethodKind == MethodKind.UserDefinedOperator && !IsOperatorGroup(definition)
                ? definition.GetNameOverride(_target)
                : null;

        return new IrUserOperator(
            TypeReferenceTo(method.ContainingType),
            definition.Name,
            IrMethodExtractor.OperatorKindOf(definition)!.Value,
            overload,
            pin,
            ConversionNaming.Of(definition, _target)
        );
    }

    /// <summary>
    /// Whether the operator belongs to a dispatched group under this target's
    /// grouping policy, whose <c>[Name]</c> pins a fast path's suffix rather
    /// than the helper's name (spec 013 FR-005).
    /// </summary>
    private bool IsOperatorGroup(IMethodSymbol definition) =>
        MethodOverloadGroups.GroupOf(definition, _context.GroupingPolicy) is { IsDispatched: true };

    // ── Operators ─────────────────────────────────────────────────────────

    /// <summary>
    /// <c>left op right</c> bound to a user-defined operator: the application
    /// over Roslyn's operands. <c>null</c> for any other binary expression,
    /// for a test against a constant <c>null</c> (<c>x == null</c> stays a
    /// plain null check, maintainer decision D4), and for a lifted operator
    /// other than <c>==</c> / <c>!=</c>, which the lifted lowering wraps
    /// around the application (<see cref="BuildBinary"/>). A conditional
    /// <c>&amp;&amp;</c> / <c>||</c> through user <c>true</c> / <c>false</c>
    /// operators reports <c>MS0001</c> where the target reports the
    /// unsupported shapes, and keeps its existing lowering elsewhere.
    /// </summary>
    private IrExpression? TryExtractUserBinary(BinaryExpressionSyntax bin, IrBinaryOp op)
    {
        if (_semantic.GetOperation(bin) is not IBinaryOperation operation)
            return null;

        if (UnsupportedUserOperator(operation.OperatorMethod, bin) is { } unsupported)
            return unsupported;

        if (AppliedUserOperator(operation.OperatorMethod) is not { } method)
            return null;

        if (bin.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
        {
            return _context.KeepsUserOperatorSourceForms
                ? null
                : ReportUnsupported(
                    bin,
                    bin.Kind().ToString(),
                    $"'{bin}' uses the user-defined 'true' / 'false' operators, which are not "
                        + "supported (baseline FR-077)."
                );
        }

        if (op.IsEquality() && (IsNullLiteral(bin.Left) || IsNullLiteral(bin.Right)))
            return null;

        if (operation.IsLifted && !op.IsEquality())
            return null;

        return new IrUserOperatorApplication(
            UserOperatorOf(method),
            [Extract(bin.Left), Extract(bin.Right)],
            BinaryOp: op,
            IsLifted: operation.IsLifted
        );
    }

    /// <summary>
    /// The application a lifted lowering applies to its non-null operands
    /// when <paramref name="operatorSyntax"/> binds a user-defined operator;
    /// <c>null</c> otherwise.
    /// </summary>
    private IrUserOperatorApplication? UserBinaryOver(
        ExpressionSyntax operatorSyntax,
        IrBinaryOp op,
        IrExpression left,
        IrExpression right
    )
    {
        var method = _semantic.GetOperation(operatorSyntax) switch
        {
            IBinaryOperation binary => binary.OperatorMethod,
            ICompoundAssignmentOperation compound => compound.OperatorMethod,
            _ => null,
        };

        return AppliedUserOperator(method) is { } applied
            ? new IrUserOperatorApplication(UserOperatorOf(applied), [left, right], BinaryOp: op)
            : null;
    }

    /// <summary>
    /// <c>op operand</c> (<c>-x</c>, <c>+x</c>, <c>!x</c>, <c>~x</c>) bound to
    /// a user-defined operator, lifted around the application over a nullable
    /// value; <c>null</c> for any other unary expression. A unary <c>+</c>
    /// has no <see cref="IrUnaryOp"/>: its application carries neither
    /// operator.
    /// </summary>
    private IrExpression? TryExtractUserUnary(PrefixUnaryExpressionSyntax node)
    {
        if (_semantic.GetOperation(node) is not IUnaryOperation operation)
            return null;

        if (UnsupportedUserOperator(operation.OperatorMethod, node) is { } unsupported)
            return unsupported;

        if (AppliedUserOperator(operation.OperatorMethod) is not { } method)
            return null;

        var user = UserOperatorOf(method);
        var op = MapUnaryOp(node.Kind());

        IrExpression Apply(IrExpression operand) =>
            new IrUserOperatorApplication(user, [operand], UnaryOp: op);

        return operation.IsLifted
            ? LiftUnary(OperandOf(node.Operand), Apply)
            : Apply(Extract(node.Operand));
    }

    /// <summary>
    /// <c>x++</c>, <c>++x</c>, <c>x--</c> or <c>--x</c> bound to a
    /// user-defined operator: <c>x = T.op(x)</c>, the target's receiver and
    /// index evaluated once and a used postfix value the one read before the
    /// store (<see cref="WriteIncrement"/>); <c>null</c> for any other
    /// increment. <paramref name="sourceForm"/> is the increment as the
    /// source writes it.
    /// </summary>
    private IrExpression? TryExtractUserIncrement(
        ExpressionSyntax node,
        IrUnaryOp op,
        IrExpression target,
        bool isPrefix,
        Func<IrExpression> sourceForm
    )
    {
        if (_semantic.GetOperation(node) is not IIncrementOrDecrementOperation operation)
            return null;

        if (UnsupportedUserOperator(operation.OperatorMethod, node) is { } unsupported)
            return unsupported;

        if (AppliedUserOperator(operation.OperatorMethod) is not { } method)
            return null;

        var user = UserOperatorOf(method);

        IrExpression Step(IrExpression current)
        {
            var applied = new IrUserOperatorApplication(user, [current], UnaryOp: op);

            return operation.IsLifted
                ? new IrConditionalExpression(IsNull(current), NullLiteral, applied)
                : applied;
        }

        return new IrUserOperatorStore(sourceForm(), WriteIncrement(node, target, isPrefix, Step));
    }

    /// <summary>
    /// <c>x op= y</c> bound to a user-defined operator:
    /// <c>x = Out(T.op(In(x), y))</c> with Roslyn's in and out conversions of
    /// the compound operation (either may be user-defined), through the
    /// read-modify-write that evaluates the target's receiver and index once;
    /// <c>null</c> for any other compound assignment.
    /// </summary>
    private IrExpression? TryExtractUserCompoundAssignment(AssignmentExpressionSyntax assign)
    {
        if (_semantic.GetOperation(assign) is not ICompoundAssignmentOperation operation)
            return null;

        if (UnsupportedUserOperator(operation.OperatorMethod, assign) is { } unsupported)
            return unsupported;

        if (
            AppliedUserOperator(operation.OperatorMethod) is not { } method
            || TryMapCompoundAssignmentToBinary(assign.Kind()) is not { } op
        )
            return UnappliedUserCompoundAssignment(assign, operation.OperatorMethod);

        var target = Extract(assign.Left);

        if (IsUnsupportedPlaceholder(target))
            return target;

        var right = Extract(assign.Right);
        var user = UserOperatorOf(method);
        var sourceForm = new IrBinaryExpression(target, MapAssignmentOp(assign.Kind()), right);

        IrExpression Apply(IrExpression current, IrExpression value) =>
            UserConversionOf(
                operation.OutConversion,
                new IrUserOperatorApplication(
                    user,
                    [UserConversionOf(operation.InConversion, current), value],
                    BinaryOp: op
                )
            );

        var readModifyWrite = BuildReadModifyWrite(target, assign, current =>
            operation.IsLifted
                ? LiftBinary(
                    new OperatorOperand(current, IsNullableValueOperand(assign.Left), assign.Left),
                    new OperatorOperand(right, IsNullableValueOperand(assign.Right), assign.Right),
                    new LiftedOperator(IsRelational: false, (left, value) =>
                        Apply(left.Value, value.Value)
                    )
                )
                : Apply(current, right)
        );

        return new IrUserOperatorStore(sourceForm, readModifyWrite);
    }

    /// <summary>
    /// <c>x op= y</c> through a user-defined operator the extraction does not
    /// apply (one of a BCL, <c>[External]</c> or <c>[PlainObject]</c> type, or
    /// a C# 14 instance operator), on a target that keeps user operators as
    /// the source wrote them
    /// (<see cref="IrExtractionContext.KeepsUserOperatorSourceForms"/>): a
    /// store whose only form is the source's, which that target prints as
    /// the instance-helper call it printed before (<c>x = x.$add(y)</c>).
    /// <c>null</c> on any other target, and for an operator other than
    /// <c>+=</c>, <c>-=</c>, <c>*=</c>, <c>/=</c> and <c>%=</c>.
    /// </summary>
    private IrExpression? UnappliedUserCompoundAssignment(
        AssignmentExpressionSyntax assign,
        IMethodSymbol? method
    )
    {
        if (
            !_context.KeepsUserOperatorSourceForms
            || method is not { MethodKind: MethodKind.UserDefinedOperator }
            || !HasInstanceHelper(assign.Kind())
        )
            return null;

        var target = Extract(assign.Left);

        if (IsUnsupportedPlaceholder(target))
            return target;

        var sourceForm = new IrBinaryExpression(
            target,
            MapAssignmentOp(assign.Kind()),
            Extract(assign.Right)
        );

        return new IrUserOperatorStore(sourceForm, sourceForm);
    }

    private static bool HasInstanceHelper(SyntaxKind compoundAssignment) =>
        compoundAssignment
            is SyntaxKind.AddAssignmentExpression
                or SyntaxKind.SubtractAssignmentExpression
                or SyntaxKind.MultiplyAssignmentExpression
                or SyntaxKind.DivideAssignmentExpression
                or SyntaxKind.ModuloAssignmentExpression;

    // ── Conversions ───────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="value"/> with the implicit user-defined conversion
    /// Roslyn reports for <paramref name="expression"/>; <c>null</c> when it
    /// reports none. A parenthesized, <c>checked</c> / <c>unchecked</c> or
    /// null-forgiving expression leaves the conversion to its operand.
    /// </summary>
    private IrExpression? WithImplicitUserConversion(
        ExpressionSyntax expression,
        IrExpression value
    )
    {
        if (
            expression is ParenthesizedExpressionSyntax or CheckedExpressionSyntax
            || expression.IsKind(SyntaxKind.SuppressNullableWarningExpression)
        )
            return null;

        var conversion = _semantic.GetConversion(expression);

        if (
            conversion.IsUserDefined
            && UnsupportedUserOperator(conversion.MethodSymbol, expression) is { } unsupported
        )
            return unsupported;

        if (
            !conversion.IsUserDefined
            || !conversion.IsImplicit
            || AppliedUserOperator(conversion.MethodSymbol) is not { } method
        )
            return null;

        var typeInfo = _semantic.GetTypeInfo(expression);

        return UserConversion(
            method,
            value,
            typeInfo.Type,
            typeInfo.ConvertedType,
            sourceForm: value,
            isLifted: IsLiftedOver(typeInfo.Type, method)
        );
    }

    /// <summary>
    /// An explicit cast of <paramref name="inner"/>, the extracted operand,
    /// bound to a user-defined conversion; <c>null</c> for any other cast. A
    /// nullable source over an operator that takes the non-nullable value
    /// yields <c>null</c> for <c>null</c> into a class (and lifts into a
    /// nullable value), and throws for <c>null</c> into a struct, as
    /// <c>.Value</c> does. Its source form is the cast as it lowered without
    /// the operator.
    /// </summary>
    private IrExpression? TryExtractUserCast(
        CastExpressionSyntax cast,
        IrExpression inner,
        ITypeSymbol sourceType,
        ITypeSymbol targetType
    )
    {
        if (ConversionAt(cast) is not { } castConversion)
            return null;

        if (UserConversionOf(castConversion) is not { } operation)
            return null;

        if (UnsupportedUserOperator(operation.OperatorMethod, cast) is { } unsupported)
            return unsupported;

        if (AppliedUserOperator(operation.OperatorMethod) is not { } method)
            return null;

        var unwraps = IsLiftedOver(sourceType, method);
        var nullTarget = targetType.IsReferenceType || targetType.IsNullableValueType();
        var yieldsNull = unwraps && nullTarget;
        var throwsOnNull = unwraps && !yieldsNull;
        var sourceUnwraps = IsNullableUnwrapCast(cast, sourceType, targetType);
        var operandType = CastOperandType(cast, sourceType);

        var unwrapped =
            throwsOnNull || sourceUnwraps ? ValueOrThrowWhenMissing(cast.Expression, inner) : inner;

        var sourceForm = new IrCastExpression(
            sourceUnwraps ? unwrapped : inner,
            ExtractTargetType(cast),
            ReferenceConversion: ReferenceConversionOf(cast, targetType)
        );

        var reachedType = unwraps ? operandType.UnwrapNullableValueType() : operandType;

        return UserConversion(
            method,
            throwsOnNull ? unwrapped : inner,
            reachedType,
            targetType,
            sourceForm,
            isLifted: yieldsNull,
            after: ExplicitNumericStepBetween(
                method.ReturnType,
                targetType,
                castConversion.IsChecked
            ),
            before: ExplicitNumericStepBetween(
                reachedType,
                method.Parameters[0].Type,
                CheckedContexts.IsChecked(cast, _semantic.Compilation)
            )
        );
    }

    /// <summary>
    /// The type the extracted operand of <paramref name="cast"/> reaches: the
    /// type Roslyn converts it to when it reports an implicit conversion of
    /// the operand (<c>char</c> to <c>int</c> before <c>explicit operator
    /// T(int)</c>), which the operand's extraction already applied;
    /// <paramref name="sourceType"/> otherwise.
    /// </summary>
    private ITypeSymbol CastOperandType(CastExpressionSyntax cast, ITypeSymbol sourceType)
    {
        var conversion = _semantic.GetConversion(cast.Expression);

        if (!conversion.IsImplicit || conversion.IsIdentity)
            return sourceType;

        return _semantic.GetTypeInfo(cast.Expression).ConvertedType ?? sourceType;
    }

    /// <summary>
    /// The outermost conversion a cast performs. Roslyn reports no operation
    /// for a cast that runs a user conversion and then a numeric one
    /// (<c>(byte)zpt</c> over <c>explicit operator int(ZPt)</c>): both
    /// conversions share the cast's syntax, so they are found in the
    /// operation of the nearest enclosing node that has one.
    /// </summary>
    private IConversionOperation? ConversionAt(CastExpressionSyntax cast)
    {
        if (_semantic.GetOperation(cast) is { } operation)
            return operation as IConversionOperation;

        for (var node = cast.Parent; node is not null; node = node.Parent)
        {
            if (_semantic.GetOperation(node) is { } enclosing)
            {
                return enclosing
                    .DescendantsAndSelf()
                    .OfType<IConversionOperation>()
                    .FirstOrDefault(conversion => conversion.Syntax == cast);
            }
        }

        return null;
    }

    /// <summary>
    /// The user-defined conversion of a cast's <paramref name="conversion"/>:
    /// itself, or the one of the same cast whose result the numeric
    /// conversion converts; <c>null</c> when the cast runs none (a cast of a
    /// value an inner cast converted is that inner cast's own).
    /// </summary>
    private static IConversionOperation? UserConversionOf(IConversionOperation conversion)
    {
        if (conversion.Conversion.IsUserDefined)
            return conversion;

        return
            conversion.Operand is IConversionOperation { Conversion.IsUserDefined: true } inner
            && inner.Syntax == conversion.Syntax
            ? inner
            : null;
    }

    /// <summary>
    /// The variable of <c>foreach (Money m in ints)</c> declared from
    /// <paramref name="element"/>, the raw element, through Roslyn's
    /// user-defined element conversion; <c>null</c> for any other element
    /// conversion.
    /// </summary>
    internal IrUserConversion? UserElementConversion(
        Conversion conversion,
        IrExpression element,
        ITypeSymbol? elementType,
        ITypeSymbol? variableType
    ) =>
        conversion.IsUserDefined && AppliedUserOperator(conversion.MethodSymbol) is { } method
            ? UserConversion(
                method,
                element,
                elementType,
                variableType,
                sourceForm: element,
                isLifted: IsLiftedOver(elementType, method)
            )
            : null;

    /// <summary>
    /// The left operand of <c>value ?? fallback</c> through the user-defined
    /// conversion C# applies to it before the fallback
    /// (<c>ICoalesceOperation.ValueConversion</c>), lifted so a <c>null</c>
    /// value still reaches the fallback; <c>null</c> when the conversion is
    /// not user-defined.
    /// </summary>
    private IrUserConversion? UserCoalescedValue(ICoalesceOperation coalesce, IrExpression value)
    {
        var conversion = coalesce.ValueConversion;

        if (
            !conversion.IsUserDefined
            || AppliedUserOperator(conversion.MethodSymbol) is not { } method
        )
            return null;

        return UserConversion(
            method,
            value,
            coalesce.Value.Type.UnderlyingValueType(),
            coalesce.Type,
            sourceForm: value,
            isLifted: true
        );
    }

    /// <summary>
    /// <paramref name="value"/> through <paramref name="conversion"/>, a
    /// conversion Roslyn reports on an operation (a compound assignment's in
    /// and out conversions, a <c>foreach</c> element), when it is
    /// user-defined; <paramref name="value"/> otherwise.
    /// </summary>
    private IrExpression UserConversionOf(CommonConversion conversion, IrExpression value) =>
        conversion.IsUserDefined && AppliedUserOperator(conversion.MethodSymbol) is { } method
            ? UserConversion(
                method,
                value,
                method.Parameters[0].Type,
                method.ReturnType,
                sourceForm: value,
                isLifted: false
            )
            : value;

    /// <summary>
    /// The conversion of <paramref name="value"/> of type
    /// <paramref name="sourceType"/> to <paramref name="targetType"/> through
    /// <paramref name="method"/>: the numeric conversion to the operator's
    /// parameter applied to the operand, and the one from its result to the
    /// target type recorded as <see cref="IrUserConversion.After"/>, each where
    /// spec 012 or FR-071 / FR-080 needs a node.
    /// </summary>
    private IrUserConversion UserConversion(
        IMethodSymbol method,
        IrExpression value,
        ITypeSymbol? sourceType,
        ITypeSymbol? targetType,
        IrExpression sourceForm,
        bool isLifted,
        IrNumericStep? after = null,
        IrNumericStep? before = null
    )
    {
        var parameterType = method.Parameters[0].Type;
        var operand = value;

        if ((before ?? NumericStepBetween(sourceType, parameterType)) is { } step)
        {
            operand = new IrNumericConversion(
                value,
                step.From,
                step.To,
                step.IsChecked,
                IsLifted: false
            );
        }

        return new IrUserConversion(
            UserOperatorOf(method),
            operand,
            after ?? NumericStepBetween(method.ReturnType, targetType),
            isLifted,
            sourceForm
        );
    }

    /// <summary>
    /// The explicit numeric conversion a cast applies to a user conversion's
    /// result (<c>(byte)</c> of an <c>int</c>, checked in a checked context),
    /// when the core lowers it for this target; <c>null</c> otherwise.
    /// </summary>
    private IrNumericStep? ExplicitNumericStepBetween(
        ITypeSymbol? from,
        ITypeSymbol? to,
        bool isChecked
    )
    {
        if (
            NumericEndpoints.Of(from.UnderlyingValueType()) is not { } source
            || NumericEndpoints.Of(to.UnderlyingValueType()) is not { } target
            || source == target
            || !_context.NumericLowering.IsLoweredExplicitly(source, target)
        )
            return null;

        return new IrNumericStep(source, target, isChecked);
    }

    /// <summary>
    /// The implicit numeric conversion between two numeric types the core
    /// makes explicit for this target; <c>null</c> when none is needed.
    /// </summary>
    private IrNumericStep? NumericStepBetween(ITypeSymbol? from, ITypeSymbol? to)
    {
        if (
            NumericEndpoints.Of(from.UnderlyingValueType()) is not { } source
            || NumericEndpoints.Of(to.UnderlyingValueType()) is not { } target
            || source == target
            || !_context.NumericLowering.NeedsImplicitNode(source, target)
        )
            return null;

        return new IrNumericStep(source, target, IsChecked: false);
    }

    /// <summary>
    /// Whether a value of <paramref name="sourceType"/>, a nullable value,
    /// reaches <paramref name="method"/>, which takes the non-nullable one:
    /// a lifted conversion, which passes <c>null</c> through.
    /// </summary>
    private static bool IsLiftedOver(ITypeSymbol? sourceType, IMethodSymbol method) =>
        sourceType.IsNullableValueType() && !method.Parameters[0].Type.IsNullableValueType();
}
