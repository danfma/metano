using Metano.Compiler.Extraction;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// The neutral fact the runtime-question scan records about a boxing
/// conversion for MS0058 (spec 011 FR-050): whether a type question reads its
/// value locally (never data-flow based). Which slots box is
/// <see cref="BoxingSlots"/>.
/// </summary>
internal static class BoxingFacts
{
    /// <summary>
    /// Whether a type question reads the value of <paramref name="conversion"/>:
    /// it is the direct operand of a test, <c>as</c>, cast, pattern, <c>switch</c>
    /// or <c>GetType()</c>, or initializes a local whose only uses in the same
    /// body are such operands.
    /// </summary>
    public static bool FeedsTypeQuestion(IConversionOperation conversion)
    {
        var (user, child) = UserOf(conversion);

        if (IsTypeQuestionOver(user, child))
            return true;

        return user
                is IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator }
            && IsQuestionedOnly(declarator);
    }

    /// <summary>
    /// The operation that consumes <paramref name="operation"/>'s value, past
    /// parentheses and implicit conversions, with the child it consumes.
    /// </summary>
    private static (IOperation? User, IOperation Child) UserOf(IOperation operation)
    {
        var child = operation;
        var parent = operation.Parent;

        while (
            parent is IParenthesizedOperation
            || parent is IConversionOperation { IsImplicit: true, IsTryCast: false }
        )
        {
            child = parent;
            parent = parent.Parent;
        }

        return (parent, child);
    }

    private static bool IsTypeQuestionOver(IOperation? user, IOperation child) =>
        user switch
        {
            IIsTypeOperation isType => isType.ValueOperand == child,
            IIsPatternOperation isPattern => isPattern.Value == child,
            ISwitchOperation switchStatement => switchStatement.Value == child,
            ISwitchExpressionOperation switchExpression => switchExpression.Value == child,
            IConversionOperation conversion => !conversion.IsImplicit || conversion.IsTryCast,
            IInvocationOperation invocation => invocation.Instance == child
                && IrExpressionExtractor.IsObjectGetType(invocation.TargetMethod),
            _ => false,
        };

    private static bool IsQuestionedOnly(IVariableDeclaratorOperation declarator)
    {
        var local = declarator.Symbol;
        var body = BodyOf(declarator);

        if (body is null)
            return false;

        var references = body.DescendantsAndSelf()
            .OfType<ILocalReferenceOperation>()
            .Where(reference => SymbolEqualityComparer.Default.Equals(reference.Local, local))
            .ToList();

        return references.Count > 0
            && references.All(reference =>
            {
                var (user, child) = UserOf(reference);

                return IsTypeQuestionOver(user, child);
            });
    }

    private static IOperation? BodyOf(IOperation operation)
    {
        var current = operation;

        while (
            current.Parent is not null
            && current.Parent
                is not (
                    IMethodBodyOperation
                    or IAnonymousFunctionOperation
                    or ILocalFunctionOperation
                )
        )
            current = current.Parent;

        return current;
    }
}
