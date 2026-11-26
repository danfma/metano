using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// The instance members every declared constructor of a class assigns: the
/// members its parameters are promoted to, and every member its body assigns
/// (<c>this.f = value</c> / <c>f = value</c>, or an <c>out this.f</c>
/// argument), on any path. A constructor that chains to
/// another one (<c>: this(...)</c>) assigns what that one assigns, so only the
/// others count; a class without a declared constructor assigns nothing.
/// Lambdas and local functions are not entered: they may never run.
/// </summary>
public static class IrConstructorAssignedMembers
{
    /// <summary>The IR names of the members every declared constructor of <paramref name="type"/> assigns.</summary>
    public static IReadOnlySet<string> Of(IrClassDeclaration type)
    {
        HashSet<string>? common = null;

        foreach (var declaration in type.Constructor?.DeclarationsInOrder ?? [])
        {
            if (declaration.ThisChain is not null)
                continue;

            var assigned = declaration
                .Parameters.Where(parameter => parameter.Promotion is not IrParameterPromotion.None)
                .Select(parameter => parameter.PromotedPropertyName ?? parameter.Parameter.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var statement in declaration.Body ?? [])
                Collect(statement, assigned);

            if (common is null)
                common = assigned;
            else
                common.IntersectWith(assigned);
        }

        return common ?? new HashSet<string>(StringComparer.Ordinal);
    }

    private static void Collect(IrStatement statement, HashSet<string> assigned)
    {
        foreach (var expression in IrNodeChildren.ExpressionsOf(statement))
            Collect(expression, assigned);

        foreach (var nested in IrNodeChildren.StatementsOf(statement))
            Collect(nested, assigned);
    }

    private static void Collect(IrExpression expression, HashSet<string> assigned)
    {
        if (AssignedMemberOf(expression) is { } member)
            assigned.Add(member);

        if (expression is IrOutArgumentCall call)
            assigned.UnionWith(
                call.OutArguments.Select(argument => argument.Target)
                    .OfType<IrOutAssignment>()
                    .Select(target => InstanceMemberOf(target.Destination))
                    .OfType<string>()
            );

        foreach (var child in IrNodeChildren.ExpressionsOf(expression))
            Collect(child, assigned);
    }

    private static string? AssignedMemberOf(IrExpression expression) =>
        expression is IrBinaryExpression { Operator: IrBinaryOp.Assign, Left: var target }
            ? InstanceMemberOf(target)
            : null;

    private static string? InstanceMemberOf(IrExpression target) =>
        target is IrMemberAccess { Target: IrThisExpression, MemberName: var member }
            ? member
            : null;
}
