using System.Collections.Immutable;
using System.Globalization;
using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The arguments of a call lowered to the exact overload Roslyn resolved
/// (spec 013 FR-017, DECIDED-21): Roslyn's bound form, one value per
/// parameter in parameter order. An omitted optional parameter passes its
/// default explicitly, an expanded <c>params</c> call passes its elements and
/// a normal-form one spreads the array it passes. The overload is selected
/// statically, so the target never rebinds an omitted default by arity.
/// Roslyn lists the arguments in evaluation order; when named arguments set
/// that order apart from parameter order, it is recorded
/// (<see cref="IrSelectedOverload.EvaluationOrder"/>) so the target keeps
/// C#'s side-effect order.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// A user-defined binary operator application bound to
    /// <paramref name="target"/>: its operands one value per parameter, in
    /// parameter order (spec 013 FR-018). <c>null</c> when
    /// <paramref name="call"/> is no binary application of that overload, or
    /// the application is a lifted one — a nullable operand that C#
    /// null-propagates, whose lowering keeps the lifted guard — so the
    /// caller keeps today's shape. A dispatched unary operator group keeps
    /// today's shape too: its call sites are not bound.
    /// </summary>
    internal BoundOverloadCall? ExtractBoundOperatorCall(
        IOperation? call,
        IMethodSymbol operatorMethod,
        IrOverloadRef target
    )
    {
        if (
            call is not IBinaryOperation { IsLifted: false } binary
            || !SymbolEqualityComparer.Default.Equals(binary.OperatorMethod, operatorMethod)
        )
            return null;

        var operands = new[] { binary.LeftOperand, binary.RightOperand };

        return new BoundOverloadCall(
            operands
                .Select(operand => new IrArgument(Extract((ExpressionSyntax)operand.Syntax)))
                .ToList(),
            new IrSelectedOverload(target)
        );
    }

    /// <summary>
    /// <paramref name="call"/> — a <c>new</c> or a constructor initializer,
    /// explicit or implicit — bound to <paramref name="target"/>, or
    /// <c>null</c> when it is no call or an argument has no form this
    /// extractor can lower (a <c>params</c> collection that is not an array),
    /// so the caller keeps the dispatcher call.
    /// </summary>
    internal BoundOverloadCall? ExtractBoundCall(IOperation? call, IrOverloadRef target)
    {
        if (
            WrittenArgumentsOf(call) is not { } bindable
            || bindable.Any(argument => !IsBindable(argument))
        )
            return null;

        var inParameterOrder = bindable
            .SelectMany(argument =>
                BoundArgumentsOf(argument)
                    .Select(value => (Value: value, argument.Parameter!.Ordinal))
            )
            .Select(
                (argument, evaluationIndex) =>
                    new BoundArgument(argument.Value, argument.Ordinal, evaluationIndex)
            )
            .OrderBy(argument => argument.ParameterOrdinal)
            .ToList();

        var evaluationOrder = OrderToKeep(
            EvaluationOrderOf(inParameterOrder),
            inParameterOrder.Select(argument => argument.Value),
            bindable
        );

        if (evaluationOrder is not null && call is not null)
            ReportIfEvaluationOrderIsLost(call, bindable);

        return new BoundOverloadCall(
            inParameterOrder.Select(argument => argument.Value).ToList(),
            new IrSelectedOverload(target, evaluationOrder)
        );
    }

    /// <summary>
    /// <see cref="SourceOrderOf"/>, reporting <c>MS0001</c> at the call when
    /// the target does not keep that order
    /// (<see cref="ReportIfEvaluationOrderIsLost"/>).
    /// </summary>
    internal IReadOnlyList<int>? ReportedSourceOrderOf(
        IOperation? call,
        IReadOnlyList<IrArgument> inParameterOrder
    )
    {
        var order = SourceOrderOf(call, inParameterOrder);

        if (order is not null && call is not null && WrittenArgumentsOf(call) is { } written)
            ReportIfEvaluationOrderIsLost(call, written);

        return order;
    }

    /// <summary>
    /// The order to keep for the arguments of a call or construction bound to
    /// no overload (spec 013 FR-017): <paramref name="inParameterOrder"/> are
    /// its arguments, one per parameter, and the result lists their positions
    /// in the order C# evaluates them. <c>null</c> when no argument is named,
    /// when the written order is parameter order, and when fewer than two
    /// <see cref="IsEvaluatedInOrder">arguments evaluated in order</see> are
    /// order-sensitive or none
    /// <see cref="HasOrderToKeep">has an order to keep</see>: the call then
    /// stays plain. Reports nothing.
    /// </summary>
    internal IReadOnlyList<int>? SourceOrderOf(
        IOperation? call,
        IReadOnlyList<IrArgument> inParameterOrder
    )
    {
        if (!HasNamedArgument(inParameterOrder) || WrittenArgumentsOf(call) is not { } written)
            return null;

        var evaluated = written.Where(IsEvaluatedInOrder).ToImmutableArray();

        if (!IsReordered(written) || CountOrderSensitiveArguments(evaluated) < 2)
            return null;

        return OrderToKeep(
            PositionsInSourceOrder(written, inParameterOrder),
            inParameterOrder.Where(argument => argument.Value is not OutArgumentSlot),
            evaluated
        );
    }

    /// <summary>
    /// <paramref name="sourceOrder"/> when it differs from parameter order
    /// and the arguments <see cref="HasOrderToKeep">have an order to
    /// keep</see>; <c>null</c> otherwise.
    /// </summary>
    private IReadOnlyList<int>? OrderToKeep(
        IReadOnlyList<int>? sourceOrder,
        IEnumerable<IrArgument> inParameterOrder,
        ImmutableArray<IArgumentOperation> arguments
    ) =>
        sourceOrder is not null && HasOrderToKeep(inParameterOrder, arguments) ? sourceOrder : null;

    /// <summary>
    /// An argument whose evaluation a reordering can move: not the receiver
    /// of a reduced extension call, which C# and every target evaluate
    /// first, and not an <c>out</c> argument, which evaluates nothing.
    /// </summary>
    private static bool IsEvaluatedInOrder(IArgumentOperation argument) =>
        argument.Parameter?.RefKind != RefKind.Out
        && argument is not { ArgumentKind: ArgumentKind.Explicit, Syntax: not ArgumentSyntax };

    /// <summary>
    /// The positions of <paramref name="inParameterOrder"/> in the order C#
    /// evaluates the <paramref name="written"/> arguments; <c>null</c> when
    /// the two lists do not line up.
    /// </summary>
    /// <remarks>
    /// They differ by a receiver in two shapes. An extension-block member
    /// passes its receiver as the first argument where Roslyn keeps it as the
    /// instance: it is evaluated first. A template of a reduced extension
    /// call places the receiver itself, where Roslyn lists it as the first
    /// argument: it leaves the order.
    /// </remarks>
    private static IReadOnlyList<int>? PositionsInSourceOrder(
        ImmutableArray<IArgumentOperation> written,
        IReadOnlyList<IrArgument> inParameterOrder
    )
    {
        var ordinals = written
            .Where(argument => !IsOmittedParamsArray(argument))
            .Select(argument => argument.Parameter?.Ordinal ?? -1)
            .ToList();

        IReadOnlyList<int>? order = (inParameterOrder.Count - ordinals.Count) switch
        {
            0 => ordinals,
            1 when inParameterOrder[0].Name is null => [0, .. ordinals.Select(o => o + 1)],
            -1 when ordinals[0] == 0 => ordinals.Skip(1).Select(o => o - 1).ToList(),
            _ => null,
        };

        return order is not null && order.Order().SequenceEqual(Enumerable.Range(0, order.Count))
            ? order
            : null;
    }

    /// <summary>
    /// The empty array Roslyn binds to a <c>params</c> parameter the call
    /// omits: it passes no element, so it has no normalized argument.
    /// </summary>
    private static bool IsOmittedParamsArray(IArgumentOperation argument) =>
        argument
            is {
                ArgumentKind: ArgumentKind.ParamArray,
                Value: IArrayCreationOperation { Initializer: null or { ElementValues.Length: 0 } }
            };

    /// <summary>
    /// The arguments Roslyn bound to a call or construction, in the order C#
    /// evaluates them; <c>null</c> for any other operation.
    /// </summary>
    private static ImmutableArray<IArgumentOperation>? WrittenArgumentsOf(IOperation? call) =>
        call switch
        {
            IObjectCreationOperation creation => creation.Arguments,
            IInvocationOperation invocation => invocation.Arguments,
            _ => null,
        };

    /// <summary>
    /// <c>MS0001</c> at a <c>: this(...)</c> chain whose named arguments are
    /// out of parameter order, for a target that passes them in parameter
    /// order (<see cref="IrExtractionContext.KeepsArgumentEvaluationOrder"/>
    /// unset): the chain's arguments are recorded as written, not bound, so
    /// <see cref="ExtractBoundCall"/> never sees them.
    /// </summary>
    internal void ReportIfThisChainOrderIsLost(ConstructorInitializerSyntax thisCall)
    {
        if (
            _semantic.GetOperation(thisCall) is IInvocationOperation invocation
            && IsReordered(invocation.Arguments)
        )
            ReportIfEvaluationOrderIsLost(invocation, invocation.Arguments);
    }

    /// <summary>
    /// Whether the written arguments, which Roslyn lists in evaluation order,
    /// are out of parameter order.
    /// </summary>
    private static bool IsReordered(ImmutableArray<IArgumentOperation> arguments)
    {
        var ordinals = arguments
            .Where(argument => argument.ArgumentKind == ArgumentKind.Explicit)
            .Select(argument => argument.Parameter?.Ordinal ?? -1)
            .ToList();

        return !ordinals.SequenceEqual(ordinals.Order());
    }

    /// <summary>
    /// <c>MS0001</c> at a reordered call for a target that passes bound
    /// arguments in parameter order
    /// (<see cref="IrExtractionContext.KeepsArgumentEvaluationOrder"/> unset)
    /// when two or more of its evaluations are order-sensitive: the target
    /// would run them in parameter order. With one, the order cannot differ.
    /// </summary>
    private void ReportIfEvaluationOrderIsLost(
        IOperation call,
        ImmutableArray<IArgumentOperation> arguments
    )
    {
        if (_context.KeepsArgumentEvaluationOrder || IsBaseInitializer(call))
            return;

        if (CountOrderSensitiveEvaluations(call, arguments) < 2)
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                "Named arguments out of parameter order with more than one evaluation that C# "
                    + "runs in written order are not supported by this target: the call passes "
                    + "them in parameter order, so they are evaluated in that order "
                    + "(spec 013 FR-017).",
                call.Syntax.GetLocation()
            )
        );
    }

    /// <summary>
    /// A <c>: base(...)</c> call or a primary-constructor base call.
    /// TODO(#572): Dart emits no base constructor call yet, so its argument
    /// order has nothing to report.
    /// </summary>
    private static bool IsBaseInitializer(IOperation call) =>
        call.Syntax is PrimaryConstructorBaseTypeSyntax
        || call.Syntax is ConstructorInitializerSyntax initializer
            && initializer.IsKind(SyntaxKind.BaseConstructorInitializer);

    /// <summary>
    /// The order-sensitive evaluations of a call: its arguments and a
    /// receiver that is itself a computation. Conservative: a receiver runs
    /// before the arguments in C# and in every target, so counting it can
    /// report a call whose order does not change.
    /// </summary>
    private static int CountOrderSensitiveEvaluations(
        IOperation call,
        ImmutableArray<IArgumentOperation> arguments
    ) =>
        CountOrderSensitiveArguments(arguments)
        + (IsOrderInsensitiveReceiver(ReceiverOf(call)) ? 0 : 1);

    /// <summary>
    /// The arguments that are not <see cref="IsOrderInsensitive">order-insensitive</see>;
    /// the receiver is not one of them.
    /// </summary>
    private static int CountOrderSensitiveArguments(ImmutableArray<IArgumentOperation> arguments) =>
        arguments.Count(argument => !IsOrderInsensitive(argument.Value));

    private static IOperation? ReceiverOf(IOperation call) =>
        call is IInvocationOperation invocation ? invocation.Instance : null;

    /// <summary>
    /// A receiver evaluates before the arguments in C# and in every target,
    /// so only one that is itself a computation counts: a static call, <c>this</c>,
    /// <c>base</c>, a local, a parameter and a null-conditional receiver do not.
    /// </summary>
    private static bool IsOrderInsensitiveReceiver(IOperation? receiver) =>
        receiver
            is null
                or IInstanceReferenceOperation
                or ILocalReferenceOperation
                or IParameterReferenceOperation
                or IConditionalAccessInstanceOperation;

    /// <summary>
    /// Whether an argument's evaluation cannot be told apart in any order: a
    /// constant (a literal, a negated literal, <c>null</c>, a <c>const</c>), a
    /// <c>default</c>, <c>this</c>, <c>typeof</c>, a lambda, or a collection
    /// made only of those. Every other expression — a local or member read
    /// included, which a call or a captured closure may write, and a
    /// user-defined conversion, which runs its operator — is order-sensitive.
    /// </summary>
    /// <remarks>
    /// Deliberately stricter than
    /// <see cref="Analysis.IrEvaluationTraits.IsSideEffectFree"/>, which
    /// counts a local read as free: a read has no side effect, but its value
    /// depends on when it runs, so here it is order-sensitive. Do not unify
    /// the two.
    /// </remarks>
    private static bool IsOrderInsensitive(IOperation operation) =>
        operation is not IConversionOperation { OperatorMethod: not null }
        && (operation.ConstantValue.HasValue || IsOrderInsensitiveShape(operation));

    private static bool IsOrderInsensitiveShape(IOperation operation) =>
        operation switch
        {
            IConversionOperation conversion => IsOrderInsensitive(conversion.Operand),
            IDefaultValueOperation
            or IInstanceReferenceOperation
            or ITypeOfOperation
            or IAnonymousFunctionOperation => true,
            IDelegateCreationOperation creation => IsOrderInsensitive(creation.Target),
            ICollectionExpressionOperation collection => collection.Elements.All(
                IsOrderInsensitive
            ),
            IArrayCreationOperation array => array.DimensionSizes.All(IsOrderInsensitive)
                && (array.Initializer?.ElementValues.All(IsOrderInsensitive) ?? true),
            _ => false,
        };

    private static bool IsBindable(IArgumentOperation argument) =>
        argument.Parameter is not null
        && argument.ArgumentKind switch
        {
            // An extension call binds its receiver as the first argument,
            // handing over the receiver expression instead of an argument
            // syntax node.
            ArgumentKind.Explicit => argument.Syntax is ArgumentSyntax or ExpressionSyntax,
            ArgumentKind.ParamArray => argument.Value is IArrayCreationOperation,
            ArgumentKind.DefaultValue => true,
            _ => false,
        };

    /// <summary>
    /// The parameter-order positions of the arguments, listed in the order C#
    /// evaluates them (<c>order[k]</c> is the position of the k-th argument
    /// evaluated); <c>null</c> when that is parameter order.
    /// </summary>
    private static IReadOnlyList<int>? EvaluationOrderOf(
        IReadOnlyList<BoundArgument> inParameterOrder
    )
    {
        var order = inParameterOrder
            .Select((argument, position) => (argument.EvaluationIndex, Position: position))
            .OrderBy(entry => entry.EvaluationIndex)
            .Select(entry => entry.Position)
            .ToList();

        return order.SequenceEqual(Enumerable.Range(0, order.Count)) ? null : order;
    }

    /// <summary>
    /// Whether arguments written out of parameter order have an order to
    /// keep: a side effect visible in the lowered shape of one of them, or
    /// <see cref="KeepsOrderForImplicitUserCode">user code an argument runs
    /// implicitly</see>. Without either, no order tells the arguments apart.
    /// </summary>
    private bool HasOrderToKeep(
        IEnumerable<IrArgument> inParameterOrder,
        ImmutableArray<IArgumentOperation> arguments
    ) =>
        inParameterOrder.Any(argument => !IrEvaluationTraits.IsSideEffectFree(argument.Value))
        || KeepsOrderForImplicitUserCode(arguments);

    /// <summary>
    /// Whether the target keeps the source order of arguments because one of
    /// them <see cref="RunsImplicitUserCode">runs user code implicitly</see>
    /// beside a second order-sensitive argument.
    /// </summary>
    /// <remarks>
    /// The lowered shape of a member read does not say whether it is a field
    /// or a property, and a concatenation does not say what it formats, so
    /// <see cref="IrEvaluationTraits.IsSideEffectFree"/> takes both for
    /// free; here the symbols decide. Implicit user code may write what
    /// another argument reads, so it is order-sensitive as soon as a second
    /// argument is not <see cref="IsOrderInsensitive">order-insensitive</see>.
    /// Field and auto-property reads alone run no code: no order tells them
    /// apart. Only a target that binds the arguments in source order
    /// (<see cref="IrExtractionContext.KeepsArgumentEvaluationOrder"/>)
    /// takes this trigger; the other targets are unchanged: such a call
    /// records no order there and reports no <c>MS0001</c>.
    /// </remarks>
    private bool KeepsOrderForImplicitUserCode(ImmutableArray<IArgumentOperation> arguments) =>
        // TODO(#592): the Dart half. A target that passes arguments in
        // parameter order should report these calls instead of skipping them.
        _context.KeepsArgumentEvaluationOrder
        && CountOrderSensitiveArguments(arguments) >= 2
        && arguments.Any(argument => RunsImplicitUserCode(argument.Value));

    /// <summary>
    /// Whether evaluating <paramref name="operation"/> runs user code its
    /// lowered shape does not show: a read of a
    /// <see cref="IsComputed">computed property</see>, a user-defined
    /// operator or conversion, or a string concatenation that formats an
    /// operand through its <c>ToString</c>. A constant and a lambda's body
    /// run nothing here.
    /// </summary>
    private static bool RunsImplicitUserCode(IOperation operation) =>
        operation switch
        {
            { ConstantValue.HasValue: true } or IAnonymousFunctionOperation => false,
            IPropertyReferenceOperation read when IsComputed(read.Property) => true,
            _ when InvokesUserOperator(operation) => true,
            IBinaryOperation concatenation when FormatsThroughToString(concatenation) => true,
            _ => operation.ChildOperations.Any(RunsImplicitUserCode),
        };

    /// <summary>A user-defined operator or conversion application.</summary>
    private static bool InvokesUserOperator(IOperation operation) =>
        operation
            is IConversionOperation { OperatorMethod: not null }
                or IUnaryOperation { OperatorMethod: not null }
                or IBinaryOperation { OperatorMethod: not null };

    /// <summary>
    /// A string concatenation with an operand whose text comes from a
    /// <c>ToString</c> that may be user code: any type but a string, a
    /// <c>bool</c>, a <c>char</c>, a number or an enum (nullable or not).
    /// </summary>
    private static bool FormatsThroughToString(IBinaryOperation operation) =>
        operation
            is { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String }
        && new[] { operation.LeftOperand, operation.RightOperand }.Any(operand =>
            !FormatsWithoutUserCode(ConcatenatedTypeOf(operand))
        );

    /// <summary>The type of a concatenation operand, under the boxing C# adds to it.</summary>
    private static ITypeSymbol? ConcatenatedTypeOf(IOperation operand) =>
        operand is IConversionOperation { IsImplicit: true, OperatorMethod: null } boxing
            ? ConcatenatedTypeOf(boxing.Operand)
            : operand.Type;

    private static bool FormatsWithoutUserCode(ITypeSymbol? type) =>
        type switch
        {
            null or { TypeKind: TypeKind.Enum } => true,
            INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable => FormatsWithoutUserCode(nullable.TypeArguments[0]),
            _ => type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_String,
        };

    private IEnumerable<IrArgument> BoundArgumentsOf(IArgumentOperation argument) =>
        argument.ArgumentKind switch
        {
            ArgumentKind.ParamArray => ExpandedParamsElementsOf(argument),
            ArgumentKind.DefaultValue => [new IrArgument(BoundDefaultOf(argument))],
            _ => [ExplicitBoundArgumentOf(argument)],
        };

    /// <summary>
    /// An argument the source passes, positional now: a <c>params</c> slot
    /// passed explicitly is the normal form, so the array spreads into the
    /// rest parameter. Every explicit argument binds an
    /// <see cref="ArgumentSyntax"/>; the one shape that hands over a bare
    /// expression instead is a reduced extension call's receiver, bound as
    /// the group's first argument, so the value read substitutes for the
    /// argument extraction.
    /// </summary>
    private IrArgument ExplicitBoundArgumentOf(IArgumentOperation argument)
    {
        var (extracted, argumentExpression) = argument.Syntax switch
        {
            ArgumentSyntax argumentSyntax => (
                ExtractArgument(argumentSyntax) with
                {
                    Name = null,
                },
                argumentSyntax.Expression
            ),
            ExpressionSyntax expression => (new IrArgument(Extract(expression)), expression),
            _ => throw new InvalidCastException(
                $"An explicit argument has unexpected syntax '{argument.Syntax?.Kind()}'."
            ),
        };
        var parameter = argument.Parameter!;

        if (!parameter.IsParams)
            return extracted;

        return NormalizeNullParamsArray(
            extracted with
            {
                IsSpread = true,
            },
            argumentExpression,
            (IMethodSymbol)parameter.ContainingSymbol
        );
    }

    /// <summary>The elements an expanded <c>params</c> call passes, one argument each.</summary>
    private IEnumerable<IrArgument> ExpandedParamsElementsOf(IArgumentOperation argument)
    {
        var elements =
            ((IArrayCreationOperation)argument.Value).Initializer?.ElementValues
            ?? ImmutableArray<IOperation>.Empty;

        return elements.Select(element => new IrArgument(Extract(ElementSyntaxOf(element))));
    }

    private static ExpressionSyntax ElementSyntaxOf(IOperation element) =>
        element.Syntax switch
        {
            ArgumentSyntax argument => argument.Expression,
            ExpressionSyntax expression => expression,
            _ => throw new InvalidOperationException(
                $"A params element has unexpected syntax '{element.Syntax.Kind()}'."
            ),
        };

    /// <summary>
    /// The value Roslyn binds to an omitted optional parameter: its constant
    /// (a caller-info parameter's included), else its declared default; an
    /// <c>[Optional]</c> parameter with no default binds <c>default</c>. A
    /// <c>default</c> of a non-nullable struct has no literal here, so it
    /// passes the omitted marker and the callee's own default applies.
    /// </summary>
    private IrExpression BoundDefaultOf(IArgumentOperation argument)
    {
        var parameter = argument.Parameter!;
        var constant = argument.Value.ConstantValue;

        var value = constant.HasValue ? constant.Value : DeclaredDefaultOf(parameter);

        return value switch
        {
            null when IsNonNullableValueType(parameter.Type) => OmittedArgument(),
            decimal amount => new IrLiteral(amount, IrLiteralKind.Decimal),
            byte or sbyte or short or ushort => new IrLiteral(
                Convert.ToInt32(value, CultureInfo.InvariantCulture),
                IrLiteralKind.Int32
            ),
            _ => BuildLiteralForDefault(value, parameter.Type),
        };
    }

    private static object? DeclaredDefaultOf(IParameterSymbol parameter) =>
        parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue : null;

    private static IrLiteral OmittedArgument() => new(null, IrLiteralKind.Default);

    private static bool IsNonNullableValueType(ITypeSymbol type) =>
        type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

    /// <summary>
    /// One bound argument, the ordinal of the parameter it binds and its
    /// position in C#'s evaluation order.
    /// </summary>
    private sealed record BoundArgument(
        IrArgument Value,
        int ParameterOrdinal,
        int EvaluationIndex
    );
}
