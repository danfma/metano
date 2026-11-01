using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether an expression is a location the code writes, decided on Roslyn's
/// bound operations rather than on syntax. Parentheses (<c>(a) = 9</c>) add
/// no operation of their own, so the write is seen on the inner operand. A
/// write is:
/// <list type="bullet">
///   <item>the target of an assignment: simple, compound, or <c>??=</c>;</item>
///   <item>the operand of an increment or decrement;</item>
///   <item>an element, at any depth, of a deconstruction target
///     (<c>(a, b) = …</c>);</item>
///   <item>a <c>ref</c> or <c>out</c> argument;</item>
///   <item>the initializer of a <c>ref</c> local (<c>ref var r = ref a</c>),
///     which can write it through the alias.</item>
/// </list>
/// </summary>
internal static class WriteAccess
{
    /// <summary>
    /// <paramref name="expression"/> is written; <c>false</c> when it binds to
    /// no operation.
    /// </summary>
    public static bool IsWriteTarget(ExpressionSyntax expression, SemanticModel model) =>
        model.GetOperation(expression) is { } operation && IsWritten(operation);

    private static bool IsWritten(IOperation operation) =>
        operation.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == operation,
            IIncrementOrDecrementOperation increment => increment.Target == operation,
            ITupleOperation tuple => IsDeconstructionTarget(tuple),
            IArgumentOperation argument => argument.Parameter?.RefKind
                is RefKind.Ref
                    or RefKind.Out,
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } =>
                declarator.Symbol.RefKind is RefKind.Ref,
            _ => false,
        };

    private static bool IsDeconstructionTarget(ITupleOperation tuple) =>
        tuple.Parent switch
        {
            IDeconstructionAssignmentOperation assignment => assignment.Target == tuple,
            ITupleOperation enclosing => IsDeconstructionTarget(enclosing),
            _ => false,
        };
}
