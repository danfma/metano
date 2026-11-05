using System.Collections.Immutable;
using System.Globalization;
using Metano.Compiler.Analysis;
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
    /// <paramref name="call"/> — a <c>new</c> or a constructor initializer,
    /// explicit or implicit — bound to <paramref name="target"/>, or
    /// <c>null</c> when it is no call or an argument has no form this
    /// extractor can lower (a <c>params</c> collection that is not an array),
    /// so the caller keeps the dispatcher call.
    /// </summary>
    internal BoundOverloadCall? ExtractBoundCall(IOperation? call, IrOverloadRef target)
    {
        ImmutableArray<IArgumentOperation>? arguments = call switch
        {
            IObjectCreationOperation creation => creation.Arguments,
            IInvocationOperation invocation => invocation.Arguments,
            _ => null,
        };

        if (arguments is not { } bindable || bindable.Any(argument => !IsBindable(argument)))
            return null;

        var inEvaluationOrder = bindable
            .SelectMany(argument =>
                BoundArgumentsOf(argument)
                    .Select(value => new BoundArgument(value, argument.Parameter!.Ordinal))
            )
            .ToList();

        var inParameterOrder = inEvaluationOrder.OrderBy(argument => argument.Parameter).ToList();

        return new BoundOverloadCall(
            inParameterOrder.Select(argument => argument.Value).ToList(),
            new IrSelectedOverload(target, EvaluationOrderOf(inEvaluationOrder, inParameterOrder))
        );
    }

    private static bool IsBindable(IArgumentOperation argument) =>
        argument.Parameter is not null
        && argument.ArgumentKind switch
        {
            ArgumentKind.Explicit => argument.Syntax is ArgumentSyntax,
            ArgumentKind.ParamArray => argument.Value is IArrayCreationOperation,
            ArgumentKind.DefaultValue => true,
            _ => false,
        };

    /// <summary>
    /// The positions, in parameter order, of the arguments as C# evaluates
    /// them; <c>null</c> when that is parameter order, or when no argument has
    /// a side effect to keep in place.
    /// </summary>
    private static IReadOnlyList<int>? EvaluationOrderOf(
        IReadOnlyList<BoundArgument> inEvaluationOrder,
        IReadOnlyList<BoundArgument> inParameterOrder
    )
    {
        var order = inEvaluationOrder
            .Select(argument => IndexOf(inParameterOrder, argument))
            .ToList();
        var isParameterOrder = order
            .Select((index, position) => index == position)
            .All(same => same);

        var hasSideEffect = inEvaluationOrder.Any(argument =>
            !IrEvaluationTraits.IsSideEffectFree(argument.Value.Value)
        );

        return isParameterOrder || !hasSideEffect ? null : order;
    }

    private static int IndexOf(IReadOnlyList<BoundArgument> arguments, BoundArgument argument)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (ReferenceEquals(arguments[index], argument))
                return index;
        }

        throw new InvalidOperationException("A bound argument is missing from parameter order.");
    }

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
    /// rest parameter.
    /// </summary>
    private IrArgument ExplicitBoundArgumentOf(IArgumentOperation argument)
    {
        var syntax = (ArgumentSyntax)argument.Syntax;
        var extracted = ExtractArgument(syntax) with { Name = null };
        var parameter = argument.Parameter!;

        if (!parameter.IsParams)
            return extracted;

        return NormalizeNullParamsArray(
            extracted with
            {
                IsSpread = true,
            },
            syntax.Expression,
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

    /// <summary>One bound argument and the ordinal of the parameter it binds.</summary>
    private sealed record BoundArgument(IrArgument Value, int Parameter);
}
