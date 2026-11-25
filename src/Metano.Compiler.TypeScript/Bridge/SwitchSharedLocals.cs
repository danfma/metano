using Metano.Compiler.Analysis;
using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// C# scopes a local declared directly in a <c>switch</c> section to the whole
/// switch block, so a later section may assign and read it without the
/// declaring section having run. In JS such a section-level <c>let</c> or
/// <c>const</c> would hit the temporal dead zone (#755), and a section that
/// binds pattern designators is emitted in a block of its own, which would
/// hide the local altogether. Each local another section uses is therefore
/// declared ahead of the switch and its declaration in the section becomes a
/// plain assignment.
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
        var shared = sw
            .Cases.SelectMany(section => SharedDeclarations(section, sw))
            .Select(declaration => declaration.Name)
            .ToHashSet();

        if (shared.Count == 0)
            return ([], sw);

        var hoistedNames = WithConstantDependencies(shared, sw);

        var declarations = SectionDeclarations(sw)
            .Where(declaration => hoistedNames.Contains(declaration.Name))
            .ToList();

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

    private static IEnumerable<IrVariableDeclaration> SectionDeclarations(IrSwitchStatement sw) =>
        sw.Cases.SelectMany(section => section.Body.OfType<IrVariableDeclaration>());

    /// <summary>
    /// <paramref name="hoisted"/> plus every constant a hoisted constant's
    /// initializer reads, transitively (#391): a hoisted constant keeps its
    /// initializer ahead of the switch, where a constant still declared in its
    /// section is out of scope. C# declares a local constant before its first
    /// use, so one pass in reverse source order reaches every dependency.
    /// </summary>
    private static HashSet<string> WithConstantDependencies(
        HashSet<string> hoisted,
        IrSwitchStatement sw
    )
    {
        var names = new HashSet<string>(hoisted);
        var constants = SectionDeclarations(sw).Where(declaration => declaration.IsConst).ToList();

        foreach (var constant in Enumerable.Reverse(constants))
        {
            if (IsReadByHoistedConstant(constant.Name, constants, names))
                names.Add(constant.Name);
        }

        return names;
    }

    private static bool IsReadByHoistedConstant(
        string name,
        IReadOnlyList<IrVariableDeclaration> constants,
        HashSet<string> hoisted
    ) =>
        constants.Any(constant =>
            hoisted.Contains(constant.Name)
            && constant.Initializer is { } initializer
            && IrReferencedNames.AnyIn(initializer, [name])
        );

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
