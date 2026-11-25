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
/// plain assignment. An <c>out var</c> of a call in statement position
/// declares its locals too: when one of them is hoisted, all of them are, and
/// the call stores into them instead of destructuring.
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

        var hoistedNames = WithOutDeclarationSiblings(WithConstantDependencies(shared, sw), sw);

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
            .Body.SelectMany(DeclarationsOf)
            .Where(declaration => IsUsedInAnotherSection(declaration.Name, section, sw));

    private static IEnumerable<IrVariableDeclaration> SectionDeclarations(IrSwitchStatement sw) =>
        sw.Cases.SelectMany(section => section.Body.SelectMany(DeclarationsOf));

    /// <summary>
    /// The locals a section-level statement declares: a variable declaration,
    /// or the <c>out var</c> locals of a call in statement position (#755),
    /// as uninitialized declarations of their types.
    /// </summary>
    private static IEnumerable<IrVariableDeclaration> DeclarationsOf(IrStatement statement) =>
        statement switch
        {
            IrVariableDeclaration declaration => [declaration],
            IrTupleDeconstruction { Initializer: IrOutArgumentCall call } => OutLocalsOf(call)
                .Select(local => new IrVariableDeclaration(local.Name, local.Type, null)),
            _ => [],
        };

    private static IEnumerable<IrOutLocalDeclaration> OutLocalsOf(IrOutArgumentCall call) =>
        call.OutArguments.Select(argument => argument.Target).OfType<IrOutLocalDeclaration>();

    /// <summary>
    /// <paramref name="hoisted"/> plus every other <c>out var</c> local of a
    /// call that declares a hoisted one: the call then stores into all of
    /// them, so none may stay declared in its section.
    /// </summary>
    private static HashSet<string> WithOutDeclarationSiblings(
        HashSet<string> hoisted,
        IrSwitchStatement sw
    )
    {
        var names = new HashSet<string>(hoisted);

        var siblings = sw
            .Cases.SelectMany(section => section.Body)
            .OfType<IrTupleDeconstruction>()
            .Select(DeclarationsOf)
            .Select(declarations => declarations.Select(declaration => declaration.Name).ToList())
            .Where(locals => locals.Any(hoisted.Contains));

        foreach (var locals in siblings)
            names.UnionWith(locals);

        return names;
    }

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
        if (statement is IrTupleDeconstruction { Initializer: IrOutArgumentCall call })
            return [StoringOutLocals(statement, call, names)];

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
    /// The <c>out var</c> call <paramref name="statement"/> as a plain call
    /// that stores into its hoisted locals; the statement itself when none of
    /// its locals is hoisted.
    /// </summary>
    private static IrStatement StoringOutLocals(
        IrStatement statement,
        IrOutArgumentCall call,
        HashSet<string> names
    )
    {
        if (!OutLocalsOf(call).Any(local => names.Contains(local.Name)))
            return statement;

        var storing = call with
        {
            OutArguments =
            [
                .. call.OutArguments.Select(argument =>
                    argument.Target is IrOutLocalDeclaration local
                        ? argument with
                        {
                            Target = new IrOutAssignment(new IrIdentifier(local.Name)),
                        }
                        : argument
                ),
            ],
        };

        return new IrExpressionStatement(storing);
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
