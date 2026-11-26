using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether a <c>var</c> local of a reference type is assigned a value that may
/// be <c>null</c> after its declaration (#796). C# declares such a local
/// nullable-annotated (<c>var current = start;</c> is a <c>Node?</c>), so a later
/// <c>current = current.Next;</c> is legal, where a target that types the local by
/// its initializer would reject the store. A local never reassigned, or reassigned
/// only with values that are not null, needs no annotation.
/// </summary>
internal static class MaybeNullLocalAssignments
{
    public static bool AnyAfterDeclaration(
        SemanticModel model,
        VariableDeclarationSyntax declaration,
        VariableDeclaratorSyntax variable
    )
    {
        if (
            !declaration.Type.IsVar
            || model.GetDeclaredSymbol(variable)
                is not ILocalSymbol { Type.IsReferenceType: true } local
        )
            return false;

        return ScopeOf(declaration)
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => StoresMaybeNull(model, assignment, local));
    }

    private static SyntaxNode ScopeOf(VariableDeclarationSyntax declaration) =>
        declaration.Parent is ForStatementSyntax or UsingStatementSyntax
            ? declaration.Parent
            : declaration.Parent?.Parent ?? declaration;

    private static bool StoresMaybeNull(
        SemanticModel model,
        AssignmentExpressionSyntax assignment,
        ILocalSymbol local
    ) =>
        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
        && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, local)
        && MayBeNull(model, assignment.Right);

    /// <summary>
    /// A suppressed value (<c>s = null!;</c>) is never null to C#, and the store
    /// keeps its assertion (#794), so it does not make the local nullable.
    /// </summary>
    private static bool MayBeNull(SemanticModel model, ExpressionSyntax value) =>
        !value.IsKind(SyntaxKind.SuppressNullableWarningExpression)
        && (
            model.GetConstantValue(value) is { HasValue: true, Value: null }
            || model.GetTypeInfo(value).Nullability.FlowState == NullableFlowState.MaybeNull
        );
}
