using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>The C# sites that request default text, shared by extraction and reachability.</summary>
internal static class DefaultTextSites
{
    public static bool IsInvocation(IMethodSymbol? method) =>
        method is { Name: nameof(ToString), IsStatic: false, Parameters.Length: 0 }
        && method.ContainingAssembly.IsBclAssembly();

    public static bool IsJoin(IMethodSymbol? method) =>
        method
            is {
                Name: "Join",
                IsStatic: true,
                Parameters.Length: 2,
                ContainingType.SpecialType: SpecialType.System_String,
            };

    public static DefaultTextSite Classify(IOperation? site) =>
        site switch
        {
            IInterpolationOperation => DefaultTextSite.Implicit,
            // A hole of a string converted to an interpolated-string handler binds as the
            // `AppendFormatted` call of the handler, not as an interpolation operation.
            IInvocationOperation { Parent: IInterpolatedStringAppendOperation } =>
                DefaultTextSite.Implicit,
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Add,
                OperatorMethod: null,
                Type.SpecialType: SpecialType.System_String
            } => DefaultTextSite.Implicit,
            ICompoundAssignmentOperation
            {
                OperatorKind: BinaryOperatorKind.Add,
                OperatorMethod: null,
                Type.SpecialType: SpecialType.System_String
            } => DefaultTextSite.Implicit,
            IInvocationOperation invocation when IsInvocation(invocation.TargetMethod) =>
                DefaultTextSite.Invocation,
            IInvocationOperation invocation when IsJoin(invocation.TargetMethod) =>
                DefaultTextSite.Implicit,
            _ => DefaultTextSite.None,
        };

    public static DefaultTextSite ForOperand(IOperation? operand)
    {
        var parent = operand?.Parent;

        while (parent is IConversionOperation or IParenthesizedOperation)
            parent = parent.Parent;

        if (parent is IArgumentOperation argument)
            parent = argument.Parent;

        return Classify(parent);
    }

    public static DefaultTextSite ForOperand(ExpressionSyntax operand, SemanticModel semantic)
    {
        // Roslyn exposes the inner operation, not operations for these transparent syntax wrappers.
        while (operand is ParenthesizedExpressionSyntax parentheses)
            operand = parentheses.Expression;

        if (
            operand is PostfixUnaryExpressionSyntax suppression
            && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression)
        )
            return ForOperand(suppression.Operand, semantic);

        return ForOperand(semantic.GetOperation(operand));
    }
}

internal enum DefaultTextSite
{
    None,
    Implicit,
    Invocation,
}
