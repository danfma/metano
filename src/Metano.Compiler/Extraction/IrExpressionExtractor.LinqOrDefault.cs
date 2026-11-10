using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The value an <c>OrDefault</c> LINQ terminal returns when no element
/// qualifies (#601). C# returns <c>default(TSource)</c>, or the
/// <c>defaultValue</c> argument of the .NET 6 overloads. <c>null</c> is that
/// value for reference and <c>Nullable&lt;T&gt;</c> elements only, so a stage
/// whose value is anything else lists it as an argument.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string DefaultValueParameterName = "defaultValue";

    // TODO(#549): the Dart target does not lower LINQ chains, so a warning
    // about the lowered terminal would describe code it never prints.
    private bool LowersLinqChains => _target is not TargetLanguage.Dart;

    /// <summary>
    /// The arguments of an <c>OrDefault</c> stage: <c>(predicate)</c> when it
    /// returns <c>null</c> on a miss, <c>(predicate, defaultValue)</c>
    /// otherwise, the predicate slot holding the omitted-argument marker when
    /// the call has none. A <c>default</c> literal written as the default
    /// value is <c>default(TSource)</c>, like no argument at all. The result
    /// says when no literal spells that default, so the stage returns
    /// <c>null</c> where C# returns a value.
    /// </summary>
    /// <remarks>
    /// <paramref name="bound"/> are the call's arguments in the parameter
    /// order of <paramref name="method"/>, and <paramref name="sourceOrder"/>
    /// the order C# evaluates them in when it differs and can be observed.
    /// That order needs two order-sensitive arguments, so it only reaches a
    /// stage that passes both a predicate and a written default value, which
    /// are then the stage's arguments one for one.
    /// </remarks>
    private LinqStageArguments OrDefaultArguments(
        IMethodSymbol method,
        IReadOnlyList<IrArgument> bound,
        IReadOnlyList<int>? sourceOrder
    )
    {
        var arguments = ValuesOf(bound);

        var defaultValueIndex = method
            .Parameters.Select(parameter => parameter.Name)
            .ToList()
            .IndexOf(DefaultValueParameterName);

        var predicate = PredicateOf(arguments, defaultValueIndex);
        var writtenDefault = ExplicitDefaultValue(arguments, defaultValueIndex);

        var missValue =
            writtenDefault
            ?? ElementMissValue.Of(method.TypeArguments[0], BuildOrigin, MapElementType);

        if (missValue is null or IrLiteral { Kind: IrLiteralKind.Null })
            return new LinqStageArguments(
                predicate is null ? [] : [predicate],
                HasUnrepresentableMissValue: missValue is null
            );

        return new LinqStageArguments(
            [predicate ?? OmittedArgument(), missValue],
            EvaluationOrder: writtenDefault is null ? null : sourceOrder
        );
    }

    private IrTypeRef MapElementType(ITypeSymbol elementType) =>
        IrTypeRefMapper.Map(elementType, _originResolver, _target);

    private static IrExpression? PredicateOf(
        IReadOnlyList<IrExpression> arguments,
        int defaultValueIndex
    ) => arguments.Where((_, index) => index != defaultValueIndex).FirstOrDefault();

    /// <summary>
    /// The <c>defaultValue</c> argument, unless it is a <c>default</c>
    /// literal (also written <c>(default)</c> or <c>default!</c>): that one
    /// keeps the opaque marker of an omitted argument.
    /// </summary>
    private static IrExpression? ExplicitDefaultValue(
        IReadOnlyList<IrExpression> arguments,
        int defaultValueIndex
    ) =>
        defaultValueIndex >= 0 && !IsDefaultLiteral(arguments[defaultValueIndex])
            ? arguments[defaultValueIndex]
            : null;

    private static bool IsDefaultLiteral(IrExpression expression) =>
        expression
            is IrLiteral { Kind: IrLiteralKind.Default }
                or IrUnaryExpression
                {
                    Operator: IrUnaryOp.NonNullAssertion,
                    Operand: IrLiteral { Kind: IrLiteralKind.Default }
                };

    private static IReadOnlyList<IrExpression> ValuesOf(IReadOnlyList<IrArgument> arguments) =>
        arguments.Select(argument => argument.Value).ToList();
}

/// <summary>
/// The arguments a LINQ stage is lowered with, in parameter order; whether
/// the stage is an <c>OrDefault</c> terminal that returns <c>null</c> on a
/// miss because no literal spells the default of its value element; and the
/// positions of the arguments in the order C# evaluates them, when named
/// arguments set it apart from parameter order and it can be observed.
/// </summary>
internal readonly record struct LinqStageArguments(
    IReadOnlyList<IrExpression> Values,
    bool HasUnrepresentableMissValue = false,
    IReadOnlyList<int>? EvaluationOrder = null
);
