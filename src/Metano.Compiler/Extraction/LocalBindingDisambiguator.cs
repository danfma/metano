using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The target's local-naming decision, plumbed into the core extractors the
/// same way the <see cref="Metano.Annotations.TargetLanguage"/> discriminator
/// reaches them: the function maps an IR local name (the C# spelling) to the
/// name the target emits, and <see cref="PinnedNames"/> lists C# local names
/// that must not be renamed because they are part of the module surface (a
/// local promoted by <c>[ExportVarFromBody]</c>).
/// </summary>
public sealed record LocalNaming(
    Func<string, string> EmitLocalName,
    IReadOnlySet<string>? PinnedNames = null
);

/// <summary>
/// One member body's local-binding renames (#398): C# is case-sensitive but
/// the emitted local name is camelCase, so two distinct bindings can map to
/// the same emitted name (<c>var Total</c> and <c>var total</c>, or a
/// parameter <c>int total</c> and a local <c>var Total</c>). When two
/// bindings visible in the same scope chain map to the same emitted name,
/// the binding declared later — an inner scope, or a later position in the
/// same scope — takes a <c>$N</c> suffix on its IR name at its declaration
/// and at every read, so the target's naming policy emits
/// <c>total$1</c> from <c>Total$1</c> unchanged. A pinned binding (a module
/// export) never renames; a collision against one renames the other binding
/// and is reported through <see cref="PinnedCollisions"/>.
/// </summary>
public sealed record LocalBindingNames(
    IReadOnlyDictionary<ISymbol, string> RenamedBySymbol,
    IReadOnlySet<string> PinnedCollisions
);

/// <summary>
/// Walks the Roslyn bindings of one member body in scope order and produces
/// the <see cref="LocalBindingNames"/> the extractors apply at every
/// declaration and read. Target-agnostic: the casing rule arrives as a
/// <see cref="LocalNaming"/> function, so the TypeScript target passes its
/// local-naming policy and the core stays free of target casing logic.
/// </summary>
internal static class LocalBindingDisambiguator
{
    public static LocalBindingNames Disambiguate(
        SyntaxNode root,
        SemanticModel semantic,
        LocalNaming naming
    )
    {
        var bindings = CollectBindings(root, semantic, naming);
        var emittedNames = bindings.Select(b => b.EmittedName).ToHashSet(StringComparer.Ordinal);
        var renamed = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var pinnedCollisions = new HashSet<string>(StringComparer.Ordinal);

        // Every binding that must give up its emitted name, in declaration
        // order so the $N pick is deterministic.
        var yielding = new List<Binding>();

        foreach (var later in bindings)
        {
            foreach (
                var earlier in bindings.Where(b =>
                    b.Position < later.Position
                    && b.EmittedName == later.EmittedName
                    && b.Scope.Span.Contains(later.Position)
                )
            )
            {
                if (later.IsRenamable)
                {
                    yielding.Add(later);
                }
                else
                {
                    // The later binding keeps its name (a module export, or a
                    // parameter that is part of the call surface); the earlier
                    // binding yields instead, and the collision is reported.
                    if (earlier.IsRenamable)
                        yielding.Add(earlier);

                    pinnedCollisions.Add(later.EmittedName);
                }
            }
        }

        foreach (var binding in yielding.DistinctBy(b => b.Symbol).OrderBy(b => b.Position))
        {
            // The suffix candidate must be free among every emitted name the
            // body already claims — user bindings and renames done so far.
            // Compiler-synthesized temporaries never collide: they spell
            // `$out0`/`receiver$temp$0`/`__r`, and `x$N` has its `$` after a
            // user identifier, a shape no user or synthesized name takes.
            var number = 1;

            while (emittedNames.Contains(naming.EmitLocalName($"{binding.IrName}${number}")))
                number++;

            renamed[binding.Symbol] = $"{binding.IrName}${number}";
            emittedNames.Add(naming.EmitLocalName($"{binding.IrName}${number}"));
        }

        return new LocalBindingNames(renamed, pinnedCollisions);
    }

    private sealed record Binding(
        ISymbol Symbol,
        string IrName,
        string EmittedName,
        int Position,
        SyntaxNode Scope,
        bool IsRenamable
    );

    /// <summary>
    /// The body's bindings in source order: every parameter (the member's own
    /// and each lambda's), every local declarator, foreach and catch variable,
    /// pattern designator and out-var the syntax declares, plus the implicit
    /// <c>value</c> parameter of an accessor root — the parameter syntax an
    /// accessor has no equivalent of.
    /// </summary>
    private static List<Binding> CollectBindings(
        SyntaxNode root,
        SemanticModel semantic,
        LocalNaming naming
    )
    {
        var bindings = new List<Binding>();

        foreach (var node in root.DescendantNodes())
        {
            var (symbol, identifier, scope) = node switch
            {
                ParameterSyntax parameter => (
                    semantic.GetDeclaredSymbol(parameter),
                    parameter.Identifier.ValueText,
                    ScopeOfParameter(parameter)
                ),
                VariableDeclaratorSyntax declarator => (
                    semantic.GetDeclaredSymbol(declarator),
                    declarator.Identifier.ValueText,
                    ScopeOfDeclarator(declarator)
                ),
                ForEachStatementSyntax forEach => (
                    semantic.GetDeclaredSymbol(forEach),
                    forEach.Identifier.ValueText,
                    forEach
                ),
                CatchDeclarationSyntax catchDeclaration => (
                    semantic.GetDeclaredSymbol(catchDeclaration),
                    catchDeclaration.Identifier.ValueText,
                    catchDeclaration.Parent ?? catchDeclaration
                ),
                SingleVariableDesignationSyntax designation => (
                    semantic.GetDeclaredSymbol(designation),
                    designation.Identifier.ValueText,
                    ScopeOfDesignation(designation)
                ),
                _ => ((ISymbol?)null, (string?)null, (SyntaxNode?)null),
            };

            if (symbol is not (ILocalSymbol or IParameterSymbol) || identifier is null)
                continue;

            AddBinding(bindings, symbol, identifier, node.SpanStart, scope!, naming, semantic);
        }

        // An accessor root declares no parameter syntax, so its implicit
        // `value` parameter joins through its symbol.
        if (
            root is AccessorDeclarationSyntax accessor
            && semantic.GetDeclaredSymbol(accessor)
                is IMethodSymbol { Parameters.Length: > 0 } owner
        )
        {
            foreach (var parameter in owner.Parameters)
                AddBinding(
                    bindings,
                    parameter,
                    parameter.Name,
                    accessor.SpanStart,
                    accessor,
                    naming,
                    semantic
                );
        }

        bindings.Sort((left, right) => left.Position.CompareTo(right.Position));
        return bindings;
    }

    /// <summary>
    /// One declared binding: its IR name (a parameter keeps the name
    /// <see cref="IrParameterNaming"/> would give it — including its own
    /// module-function shadow suffix — any other binding its C# spelling),
    /// the emitted name the target's function produces from that, the
    /// declaration position, and the scope node the binding stays visible in.
    /// </summary>
    private static void AddBinding(
        List<Binding> bindings,
        ISymbol symbol,
        string identifier,
        int position,
        SyntaxNode scope,
        LocalNaming naming,
        SemanticModel semantic
    )
    {
        var irName = symbol is IParameterSymbol parameter
            ? IrParameterNaming.IrNameOf(parameter, semantic.Compilation)
            : identifier;

        bindings.Add(
            new Binding(
                symbol,
                irName,
                naming.EmitLocalName(irName),
                position,
                scope,
                IsRenamable(symbol, identifier, naming)
            )
        );
    }

    /// <summary>
    /// Only bindings the target declares itself may rename: every local, and
    /// the parameters <see cref="IrParameterNaming"/> already renames (a
    /// promoted constructor parameter, an <c>[ObjectArgs]</c> key or an
    /// accessor's <c>value</c> are the call surface and keep their name). A
    /// pinned name — a local promoted to a module export — is never renamed.
    /// </summary>
    private static bool IsRenamable(ISymbol symbol, string identifier, LocalNaming naming) =>
        symbol is ILocalSymbol
            && (naming.PinnedNames is null || !naming.PinnedNames.Contains(identifier))
        || symbol is IParameterSymbol parameter && IrParameterNaming.IsRenamable(parameter);

    /// <summary>
    /// A parameter's scope is its owner's whole declaration — the parameter
    /// list lives before the body, so two colliding parameters
    /// (<c>int a, int A</c>) are declared inside each other's scope.
    /// </summary>
    private static SyntaxNode ScopeOfParameter(ParameterSyntax parameter) =>
        parameter.Parent?.Parent ?? parameter;

    /// <summary>
    /// A local declaration's scope is the block (or switch section, or the
    /// compilation unit of top-level statements) that contains the
    /// statement; a <c>for</c>/<c>using</c> initializer's scope is the whole
    /// statement, since the loop body still sees it.
    /// </summary>
    private static SyntaxNode ScopeOfDeclarator(VariableDeclaratorSyntax declarator)
    {
        var container = declarator.Parent?.Parent;

        return container switch
        {
            LocalDeclarationStatementSyntax statement => ScopeOfStatementList(statement),
            ForStatementSyntax or UsingStatementSyntax => container,
            _ => container ?? declarator.Parent ?? declarator,
        };
    }

    private static SyntaxNode ScopeOfStatementList(LocalDeclarationStatementSyntax statement)
    {
        var container = statement.Parent;

        return container switch
        {
            GlobalStatementSyntax global => global.Parent ?? global,
            _ => container ?? statement,
        };
    }

    /// <summary>
    /// A pattern designator's or out-var's scope: the nearest enclosing scope
    /// container. A loop condition's designation scopes to the loop (the
    /// lowering binds a fresh local per iteration, inside it), a switch
    /// expression arm's to the arm; anything else — an out var or designator
    /// an if condition or a plain statement declares — is bound ahead of its
    /// statement in the enclosing statement list, so its scope is that list's
    /// block (or member body, catch clause, lambda).
    /// </summary>
    private static SyntaxNode ScopeOfDesignation(SingleVariableDesignationSyntax designation) =>
        designation
            .Ancestors()
            .FirstOrDefault(a =>
                a
                    is SwitchExpressionArmSyntax
                        or WhileStatementSyntax
                        or DoStatementSyntax
                        or ForStatementSyntax
                        or ForEachStatementSyntax
                        or ForEachVariableStatementSyntax
                        or LambdaExpressionSyntax
                        or BaseMethodDeclarationSyntax
                        or AccessorDeclarationSyntax
                        or CatchClauseSyntax
                        or BlockSyntax
                        or SwitchSectionSyntax
                        or CompilationUnitSyntax
            )
        ?? designation;
}
