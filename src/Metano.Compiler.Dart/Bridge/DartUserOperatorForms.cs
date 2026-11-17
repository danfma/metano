using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// How Dart prints the user-defined operator and conversion nodes (baseline
/// FR-077, FR-079): as the source wrote them, so its output is the one it
/// printed before the nodes existed. An operator application is its native
/// operator (Dart declares the operator, <c>a + b</c>, <c>-a</c>; a unary
/// <c>+</c> is its operand), an increment the native <c>x++</c>, a compound
/// <c>+=</c> / <c>-=</c> / <c>*=</c> / <c>/=</c> / <c>%=</c> the instance helper
/// (<c>t = t.$add(y)</c>) and any other compound its native operator, and a
/// conversion its operand or its cast.
/// </summary>
internal static class DartUserOperatorForms
{
    /// <summary>
    /// The source form of a user-defined operator or conversion node;
    /// <paramref name="expression"/> itself for any other node.
    /// </summary>
    public static IrExpression SourceFormOf(IrExpression expression) =>
        expression switch
        {
            IrUserOperatorApplication { BinaryOp: { } op } application => new IrBinaryExpression(
                application.Operands[0],
                op,
                application.Operands[1]
            ),
            IrUserOperatorApplication { UnaryOp: { } op } application => new IrUnaryExpression(
                op,
                application.Operands[0]
            ),
            IrUserOperatorApplication application => application.Operands[0],
            IrUserOperatorStore store => StoreFormOf(store.SourceForm),
            IrUserConversion conversion => conversion.SourceForm,
            _ => expression,
        };

    /// <summary>
    /// A <c>foreach</c> whose variable is declared from the user-defined
    /// conversion of the raw element (<c>foreach (Money m in ints)</c>) as the
    /// source wrote it: the loop over the variable itself.
    /// </summary>
    public static IrForEachStatement LoopOverSourceVariable(IrForEachStatement loop) =>
        loop.Body.FirstOrDefault()
            is IrVariableDeclaration
            {
                Initializer: IrUserConversion { SourceForm: IrIdentifier element },
            } declaration
        && element.Name == loop.Variable
            ? loop with
            {
                Variable = declaration.Name,
                VariableType = declaration.Type,
                Body = loop.Body.Skip(1).ToList(),
            }
            : loop;

    private static IrExpression StoreFormOf(IrExpression sourceForm) =>
        sourceForm is IrBinaryExpression compound
        && InstanceHelperOf(compound.Operator) is { } helper
            ? new IrBinaryExpression(
                compound.Left,
                IrBinaryOp.Assign,
                new IrCallExpression(
                    new IrMemberAccess(compound.Left, helper),
                    [new IrArgument(compound.Right)]
                )
            )
            : sourceForm;

    private static string? InstanceHelperOf(IrBinaryOp compoundOperator) =>
        compoundOperator switch
        {
            IrBinaryOp.AddAssign => "$add",
            IrBinaryOp.SubtractAssign => "$subtract",
            IrBinaryOp.MultiplyAssign => "$multiply",
            IrBinaryOp.DivideAssign => "$divide",
            IrBinaryOp.ModuloAssign => "$modulo",
            _ => null,
        };
}
