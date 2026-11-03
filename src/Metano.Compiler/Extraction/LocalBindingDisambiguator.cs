using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The target's local-naming decision, plumbed into the core extractors
/// through <see cref="IrExtractionContext"/>: the function maps an IR local
/// name (the C# spelling) to the name the target emits, and
/// <see cref="PinnedNames"/> lists C# local names that must not be renamed
/// because they are part of the module surface (a local promoted by
/// <c>[ExportVarFromBody]</c>).
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
/// <c>total$1</c> from <c>Total$1</c> unchanged. A binding that cannot be
/// renamed (a pinned module export, or a parameter that is part of the call
/// surface) keeps its name and the other binding yields; a collision where
/// NEITHER binding can yield is listed in <see cref="UnresolvableCollisions"/>
/// and reported as MS0003 — the emitted code would redeclare one name.
/// </summary>
public sealed record LocalBindingNames(
    IReadOnlyDictionary<ISymbol, string> RenamedBySymbol,
    IReadOnlySet<string> UnresolvableCollisions
)
{
    public static LocalBindingNames Empty { get; } =
        new(
            new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default),
            new HashSet<string>(StringComparer.Ordinal)
        );

    /// <summary>
    /// The IR name of a local binding: its disambiguated name when this map
    /// renamed it (#398), otherwise the fallback spelling.
    /// </summary>
    public string NameOf(ISymbol? symbol, string fallback) =>
        symbol is not null && RenamedBySymbol.TryGetValue(symbol, out var renamed)
            ? renamed
            : fallback;
}

/// <summary>
/// Walks the Roslyn bindings of one member body in scope order and produces
/// the <see cref="LocalBindingNames"/> the extractors apply at every
/// declaration and read. Target-agnostic: the casing rule arrives inside the
/// <see cref="IrExtractionContext"/>, so the TypeScript target passes its
/// local-naming policy and the core stays free of target casing logic.
/// </summary>
internal static class LocalBindingDisambiguator
{
    public static LocalBindingNames ForMember(ISymbol member, IrExtractionContext context)
    {
        if (context.LocalNaming is null || context.Compilation is null)
            return LocalBindingNames.Empty;

        var root = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (root is null)
            return LocalBindingNames.Empty;

        // A referenced assembly's declaring syntax belongs to its own
        // compilation; try the symbol-helper lookup (source references) and
        // leave the map empty when the tree is unreachable.
        var model = SymbolHelper.TryGetSemanticModel(context.Compilation, root.SyntaxTree);

        return model is null
            ? LocalBindingNames.Empty
            : Disambiguate(
                new BindingCollector(model, context.LocalNaming).Collect(root).ToList(),
                root,
                context.LocalNaming
            );
    }

    public static LocalBindingNames ForRoot(SyntaxNode root, IrExtractionContext context)
    {
        if (context.LocalNaming is null || context.Compilation is null)
            return LocalBindingNames.Empty;

        var model = SymbolHelper.TryGetSemanticModel(context.Compilation, root.SyntaxTree);

        return model is null
            ? LocalBindingNames.Empty
            : Disambiguate(
                new BindingCollector(model, context.LocalNaming).Collect(root).ToList(),
                root,
                context.LocalNaming
            );
    }

    /// <summary>
    /// The disambiguation of a flat statement list that forms one C# scope —
    /// C# 9 top-level statements — computed once over exactly those
    /// statements, not over the compilation unit that hosts them (whose other
    /// members would inflate the suffix search space).
    /// </summary>
    public static LocalBindingNames ForStatements(
        IReadOnlyList<StatementSyntax> statements,
        SemanticModel semantic,
        LocalNaming naming
    )
    {
        if (statements.Count == 0)
            return LocalBindingNames.Empty;

        var collector = new BindingCollector(semantic, naming);
        var bindings = statements.SelectMany(collector.Collect).ToList();

        return Disambiguate(bindings, statements[0], naming);
    }

    private static LocalBindingNames Disambiguate(
        List<Binding> bindings,
        SyntaxNode root,
        LocalNaming naming
    )
    {
        bindings.Sort((left, right) => left.Position.CompareTo(right.Position));

        var emittedNames = bindings.Select(b => b.EmittedName).ToHashSet(StringComparer.Ordinal);
        var renamed = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var unresolvable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var yielding in FindYieldingBindings(bindings, unresolvable, naming))
        {
            var (irName, emittedName) = NextFreeSuffixedName(yielding, emittedNames, naming);
            renamed[yielding.Symbol] = irName;
            emittedNames.Add(emittedName);
        }

        ReportUnresolvable(unresolvable, root);
        return new LocalBindingNames(renamed, unresolvable);
    }

    /// <summary>
    /// Every collision the walk sees: a pair of bindings with one emitted
    /// name whose earlier binding's scope contains the later one's
    /// declaration. The binding that must give up the name is the later one
    /// when it can be renamed, otherwise the earlier one; a collision where
    /// neither yields is unresolvable and recorded by emitted name — except
    /// one a pinned name takes part in, which the entry-point transformer
    /// reports against its <c>[ExportVarFromBody]</c> argument instead.
    /// </summary>
    private static IEnumerable<Binding> FindYieldingBindings(
        IReadOnlyList<Binding> bindings,
        ISet<string> unresolvable,
        LocalNaming naming
    )
    {
        var yielding = new List<Binding>();

        foreach (var later in bindings)
        {
            foreach (var earlier in bindings.Where(b => IsVisibleCollision(b, later)))
            {
                if (later.IsRenamable)
                {
                    yielding.Add(later);
                    continue;
                }

                if (earlier.IsRenamable)
                {
                    yielding.Add(earlier);
                    continue;
                }

                if (!IsPinned(later, naming) && !IsPinned(earlier, naming))
                    unresolvable.Add(later.EmittedName);
            }
        }

        return yielding.DistinctBy(b => b.Symbol).OrderBy(b => b.Position);
    }

    private static bool IsVisibleCollision(Binding earlier, Binding later) =>
        earlier.Position < later.Position
        && earlier.EmittedName == later.EmittedName
        && earlier.Scope.Span.Contains(later.Position);

    /// <summary>
    /// The first free <c>$N</c> suffix for the binding's IR name: free among
    /// every emitted name the body already claims — user bindings and renames
    /// done so far. Compiler-synthesized temporaries never collide: they
    /// spell <c>$out0</c>/<c>receiver$temp$0</c>/<c>__r</c>, and <c>x$N</c>
    /// has its <c>$</c> after a user identifier, a shape no user or
    /// synthesized name takes.
    /// </summary>
    private static (string IrName, string EmittedName) NextFreeSuffixedName(
        Binding binding,
        IReadOnlySet<string> emittedNames,
        LocalNaming naming
    )
    {
        var number = 1;

        while (emittedNames.Contains(naming.EmitLocalName($"{binding.IrName}${number}")))
            number++;

        var irName = $"{binding.IrName}${number}";

        return (irName, naming.EmitLocalName(irName));
    }

    private static bool IsPinned(Binding binding, LocalNaming naming) =>
        naming.PinnedNames is not null && naming.PinnedNames.Contains(binding.Identifier);

    /// <summary>
    /// Reports each unresolvable collision through the general extraction
    /// sink, at the declaring member's root — the target transformer's
    /// diagnostic list receives it wherever a body is extracted, not only at
    /// the module entry points.
    /// </summary>
    private static void ReportUnresolvable(IReadOnlySet<string> unresolvable, SyntaxNode root)
    {
        foreach (var emittedName in unresolvable)
        {
            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.AmbiguousConstruct,
                    $"Two bindings in this member emit the same name '{emittedName}' and neither "
                        + "can be renamed (parameters of the call surface); rename one of them "
                        + "in C#.",
                    root.GetLocation()
                )
            );
        }
    }

    private sealed record Binding(
        ISymbol Symbol,
        string Identifier,
        string IrName,
        string EmittedName,
        int Position,
        SyntaxNode Scope,
        bool IsRenamable
    );

    /// <summary>
    /// Collects the bindings one member body declares, in source order:
    /// every parameter (the member's own and each lambda's — a discard
    /// parameter is not a binding), every local declarator, foreach and
    /// catch variable, pattern designator and out-var the syntax declares,
    /// plus the implicit <c>value</c> parameter of an accessor root and the
    /// receiver of the C# 14 extension block a member is declared in — both
    /// part of the call surface, so never renamed.
    /// </summary>
    private sealed class BindingCollector(SemanticModel semantic, LocalNaming naming)
    {
        public IEnumerable<Binding> Collect(SyntaxNode root)
        {
            foreach (var node in root.DescendantNodes())
            {
                if (DeclaredBinding(node) is not { } declaration)
                    continue;

                if (declaration.Symbol is not (ILocalSymbol or IParameterSymbol))
                    continue;

                yield return ToBinding(
                    declaration.Symbol,
                    declaration.Identifier,
                    node.SpanStart,
                    declaration.Scope
                );
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
                    yield return ToBinding(parameter, parameter.Name, accessor.SpanStart, accessor);
            }

            // A member of a C# 14 extension block sees the block's receiver
            // everywhere; it is the call surface, so it never yields its
            // name (#398).
            if (ExtensionReceiverOf(root) is { } receiver)
                yield return ToBinding(receiver, receiver.Name, root.SpanStart, root);
        }

        private (ISymbol Symbol, string Identifier, SyntaxNode Scope)? DeclaredBinding(
            SyntaxNode node
        ) =>
            node switch
            {
                ParameterSyntax parameter
                    when semantic.GetDeclaredSymbol(parameter)
                        is IParameterSymbol { IsDiscard: false } symbol => (
                    symbol,
                    parameter.Identifier.ValueText,
                    ScopeOfParameter(parameter)
                ),
                VariableDeclaratorSyntax declarator
                    when semantic.GetDeclaredSymbol(declarator) is ILocalSymbol symbol => (
                    symbol,
                    declarator.Identifier.ValueText,
                    ScopeOfDeclarator(declarator)
                ),
                ForEachStatementSyntax forEach
                    when semantic.GetDeclaredSymbol(forEach) is ILocalSymbol symbol => (
                    symbol,
                    forEach.Identifier.ValueText,
                    forEach
                ),
                CatchDeclarationSyntax catchDeclaration
                    when semantic.GetDeclaredSymbol(catchDeclaration) is ILocalSymbol symbol => (
                    symbol,
                    catchDeclaration.Identifier.ValueText,
                    catchDeclaration.Parent ?? catchDeclaration
                ),
                SingleVariableDesignationSyntax designation
                    when semantic.GetDeclaredSymbol(designation) is ILocalSymbol symbol => (
                    symbol,
                    designation.Identifier.ValueText,
                    ScopeOfDesignation(designation)
                ),
                _ => null,
            };

        private Binding ToBinding(ISymbol symbol, string identifier, int position, SyntaxNode scope)
        {
            var irName = symbol is IParameterSymbol parameter
                ? IrParameterNaming.IrNameOf(parameter, semantic.Compilation)
                : identifier;

            return new Binding(
                symbol,
                identifier,
                irName,
                naming.EmitLocalName(irName),
                position,
                scope,
                IsRenamable(symbol, identifier, naming)
            );
        }

        /// <summary>
        /// The receiver parameter of the C# 14 <c>extension(R r)</c> block the
        /// root member is declared in, or null for an ordinary member. The
        /// block node is a preview syntax kind, so it is reached through
        /// reflection the way the module-function extractor reaches it.
        /// </summary>
        private IParameterSymbol? ExtensionReceiverOf(SyntaxNode root)
        {
            var extensionBlock = root.Ancestors()
                .FirstOrDefault(a => a.Kind().ToString() == "ExtensionBlockDeclaration");

            if (extensionBlock is null)
                return null;

            var parameterList =
                extensionBlock.GetType().GetProperty("ParameterList")?.GetValue(extensionBlock)
                as ParameterListSyntax;

            var receiverSyntax = parameterList?.Parameters.FirstOrDefault();

            return receiverSyntax is null
                ? null
                : semantic.GetDeclaredSymbol(receiverSyntax) as IParameterSymbol;
        }

        /// <summary>
        /// Only bindings the target declares itself may rename: every local
        /// that is not pinned, and the parameters the target names itself —
        /// an ordinary method's, a module function's, a lambda's (the policy
        /// <see cref="IrParameterNaming"/> already renames) and an operator's
        /// (an operator is called through the IR dispatch, never by a TS
        /// caller naming a parameter). A promoted constructor parameter, an
        /// <c>[ObjectArgs]</c> key, an accessor's <c>value</c> and an
        /// extension receiver are the call surface and keep their name.
        /// </summary>
        private static bool IsRenamable(ISymbol symbol, string identifier, LocalNaming naming) =>
            symbol switch
            {
                ILocalSymbol => naming.PinnedNames is null
                    || !naming.PinnedNames.Contains(identifier),
                IParameterSymbol parameter => IrParameterNaming.IsRenamable(parameter)
                    || parameter.ContainingSymbol
                        is IMethodSymbol
                        {
                            MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion,
                        },
                _ => false,
            };

        /// <summary>
        /// A parameter's scope is its owner's whole declaration — the
        /// parameter list lives before the body, so two colliding parameters
        /// (<c>int a, int A</c>) are declared inside each other's scope.
        /// </summary>
        private static SyntaxNode ScopeOfParameter(ParameterSyntax parameter) =>
            parameter.Parent?.Parent ?? parameter;

        /// <summary>
        /// A local declaration's scope is the block (or switch section, or
        /// the compilation unit of top-level statements) that contains the
        /// statement — a switch section's locals share the whole switch, as
        /// C# and JS scope them; a <c>for</c>/<c>using</c> initializer's
        /// scope is the whole statement, since the loop body still sees it.
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
                SwitchSectionSyntax section => section.Parent ?? section,
                _ => container ?? statement,
            };
        }

        /// <summary>
        /// A pattern designator's or out-var's scope: the nearest enclosing
        /// scope container. A loop condition's designation scopes to the loop
        /// (the lowering binds a fresh local per iteration, inside it), a
        /// switch expression arm's to the arm, a case label's to its switch
        /// section (the bridge wraps each label's bindings in <c>{}</c>);
        /// anything else — a designation a section statement or an if
        /// condition declares — is bound ahead of its statement in the
        /// enclosing statement list, so its scope is that list's block (or
        /// the whole switch for a section's statements, or member body,
        /// catch clause, lambda).
        /// </summary>
        private static SyntaxNode ScopeOfDesignation(SingleVariableDesignationSyntax designation) =>
            ScopeContainerOf(designation);

        private static SyntaxNode ScopeContainerOf(SingleVariableDesignationSyntax designation)
        {
            foreach (var ancestor in designation.Ancestors())
            {
                switch (ancestor)
                {
                    case SwitchExpressionArmSyntax:
                    case WhileStatementSyntax:
                    case DoStatementSyntax:
                    case ForStatementSyntax:
                    case ForEachStatementSyntax:
                    case ForEachVariableStatementSyntax:
                    case LambdaExpressionSyntax:
                    case BaseMethodDeclarationSyntax:
                    case AccessorDeclarationSyntax:
                    case CatchClauseSyntax:
                    case BlockSyntax:
                    case CompilationUnitSyntax:
                        return ancestor;

                    case SwitchSectionSyntax section:
                        return BelongsToCaseLabel(designation, section)
                            ? section
                            : section.Parent ?? section;
                }
            }

            return designation;
        }

        /// <summary>
        /// Whether the designation is declared by one of the section's case
        /// labels (its pattern), rather than hoisted from a section
        /// statement.
        /// </summary>
        private static bool BelongsToCaseLabel(
            SingleVariableDesignationSyntax designation,
            SwitchSectionSyntax section
        ) =>
            designation
                .Ancestors()
                .TakeWhile(ancestor => ancestor != section)
                .Any(ancestor => ancestor is CasePatternSwitchLabelSyntax);
    }
}
