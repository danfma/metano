using Metano.Compiler.Analysis;
using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// C# scopes a local declared directly in a <c>switch</c> section to the whole
/// switch block, so another section may use it. A section that binds pattern
/// designators is emitted in a block of its own, which would hide such a local
/// from the other sections. Each one is therefore declared ahead of the switch
/// and its declaration in the section becomes a plain assignment.
/// </summary>
internal static class SwitchSharedLocals
{
    /// <summary>
    /// The declarations to place before <paramref name="sw"/>, and the switch
    /// with those locals' declarations turned into assignments.
    /// </summary>
    public static (
        IReadOnlyList<IrVariableDeclaration> Declarations,
        IrSwitchStatement Switch
    ) Hoist(IrSwitchStatement sw)
    {
        var declarations = sw
            .Cases.Where(BindsDesignators)
            .SelectMany(section => SharedDeclarations(section, sw))
            .ToList();

        if (declarations.Count == 0)
            return ([], sw);

        var hoistedNames = declarations.Select(declaration => declaration.Name).ToHashSet();

        var cases = sw.Cases.Select(section => AssignHoisted(section, hoistedNames)).ToList();

        return ([.. declarations.Select(BeforeSwitch)], sw with { Cases = cases });
    }

    /// <summary>
    /// Whether the section's body is emitted in a block of its own: a single
    /// pattern label that declares locals.
    /// </summary>
    internal static bool BindsDesignators(IrSwitchCase section) =>
        section.Labels is [IrPatternCaseLabel label]
        && IrPatternDesignators.Declares(label.Pattern);

    private static IEnumerable<IrVariableDeclaration> SharedDeclarations(
        IrSwitchCase section,
        IrSwitchStatement sw
    ) =>
        section
            .Body.OfType<IrVariableDeclaration>()
            .Where(declaration => IsUsedInAnotherSection(declaration.Name, section, sw));

    private static bool IsUsedInAnotherSection(
        string name,
        IrSwitchCase owner,
        IrSwitchStatement sw
    ) =>
        sw
            .Cases.Where(section => !ReferenceEquals(section, owner))
            .Any(section => IrReferencedNames.AnyIn(section.Body, [name]));

    private static IrSwitchCase AssignHoisted(IrSwitchCase section, HashSet<string> names) =>
        section with
        {
            Body =
            [
                .. section.Body.SelectMany(statement =>
                    RewriteHoistedDeclaration(statement, names)
                ),
            ],
        };

    private static IEnumerable<IrStatement> RewriteHoistedDeclaration(
        IrStatement statement,
        HashSet<string> names
    )
    {
        if (statement is not IrVariableDeclaration declaration || !names.Contains(declaration.Name))
            return [statement];

        // Nothing left to assign: an uninitialized local, or a const already
        // declared ahead with its value.
        if (declaration.Initializer is null || declaration.IsConst)
            return [];

        return
        [
            new IrExpressionStatement(
                new IrBinaryExpression(
                    new IrIdentifier(declaration.Name),
                    IrBinaryOp.Assign,
                    declaration.Initializer
                )
            ),
        ];
    }

    /// <summary>
    /// A constant keeps its initializer, since C# needs no assignment to run
    /// before another section reads it. Any other local is declared
    /// uninitialized, with the type the compiler inferred for a <c>var</c>.
    /// </summary>
    private static IrVariableDeclaration BeforeSwitch(IrVariableDeclaration declaration) =>
        declaration.IsConst ? declaration : Uninitialized(declaration);

    private static IrVariableDeclaration Uninitialized(IrVariableDeclaration declaration) =>
        declaration with
        {
            Type = declaration.Type ?? declaration.InferredType,
            Initializer = null,
            IsConst = false,
        };
}
