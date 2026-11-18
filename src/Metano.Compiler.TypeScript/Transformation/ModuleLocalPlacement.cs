using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Decides on which side of its module-level owner the declaration of a nested type
/// declared at module level goes — a <c>private</c> one (#412) or a hoisted one (spec
/// 011 FR-040, <see cref="NestedTypeHoisting"/>) — and in which order those of one side
/// are declared. A TypeScript class declaration is not hoisted, so whatever a
/// declaration evaluates at load time must already be declared. The owner and the
/// companion namespace of the nested types it does not hoist are one block; a
/// module-level nested type is declared before that block so the owner's static
/// initializers and module constants can construct it, unless its own load-time code
/// reads the block — then it must follow it. Within a side, a declaration follows the
/// module-level declarations its load-time code reads (<see cref="Order"/>).
/// <para>
/// The load-time code of a module-level type is the <c>extends</c> clause, static field
/// and property initializers, and static constructor bodies of the type itself and of
/// the nested types declared with it in its companion namespace. Lambdas and local
/// functions inside them
/// are deferred, so they are not walked. Two load-time paths are not followed either:
/// the bodies of the methods those initializers call, and the instance initialization
/// (field initializers, constructor body) a <c>new X()</c> runs at load time — a
/// dependency reached only through them is not seen, as on main. A real cycle — the
/// owner's load-time code constructs the type and the type's load-time code reads the
/// owner — has no valid order in either direction and keeps the type after the owner.
/// </para>
/// <para>
/// A load-time cycle among the declarations of one side keeps their declaration order.
/// </para>
/// </summary>
internal sealed class ModuleLocalPlacement(
    INamedTypeSymbol owner,
    Compilation compilation,
    NestedTypeHoisting hoisting
)
{
    private readonly Dictionary<INamedTypeSymbol, bool> _followsOwner = new(
        SymbolEqualityComparer.Default
    );

    private readonly HashSet<INamedTypeSymbol> _deciding = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// True when the load-time code of the module-level <paramref name="nestedType"/>
    /// reads the owner block, or another module-level nested type that must follow it.
    /// </summary>
    public bool MustFollowOwner(INamedTypeSymbol nestedType)
    {
        if (_followsOwner.TryGetValue(nestedType, out var decided))
            return decided;

        // A load-time cycle among module-level types says nothing about the owner: the
        // type being decided counts as not following it. The results cached below while
        // a cycle is open are therefore order-dependent, by design.
        if (!_deciding.Add(nestedType))
            return false;

        var follows = LoadTimeDependencies(nestedType)
            .Any(dependency => DependsOnOwnerBlock(dependency, nestedType));

        _deciding.Remove(nestedType);
        _followsOwner[nestedType] = follows;

        return follows;
    }

    /// <summary>
    /// The module-level declarations of the owner's module that
    /// <paramref name="types"/> are on one side of, in an order where each follows the
    /// ones its load-time code reads; a cycle keeps the given order.
    /// </summary>
    public IReadOnlyList<INamedTypeSymbol> Order(IReadOnlyList<INamedTypeSymbol> types)
    {
        var side = types.ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var ordered = new List<INamedTypeSymbol>();

        void Visit(INamedTypeSymbol type)
        {
            if (!visited.Add(type))
                return;

            foreach (var dependency in ModuleLevelDependencies(type).Where(side.Contains))
                Visit(dependency);

            ordered.Add(type);
        }

        foreach (var type in types)
            Visit(type);

        return ordered;
    }

    private IEnumerable<INamedTypeSymbol> ModuleLevelDependencies(INamedTypeSymbol nestedType) =>
        LoadTimeDependencies(nestedType)
            .Select(NearestModuleLevelType)
            .OfType<INamedTypeSymbol>()
            .Where(declaration => !SymbolEqualityComparer.Default.Equals(declaration, nestedType));

    private bool DependsOnOwnerBlock(INamedTypeSymbol dependency, INamedTypeSymbol nestedType)
    {
        var declaration = NearestModuleLevelType(dependency);

        if (declaration is null)
            return IsInOwnerModule(dependency);

        var declaredWithType = SymbolEqualityComparer.Default.Equals(declaration, nestedType);

        return !declaredWithType && IsInOwnerModule(declaration) && MustFollowOwner(declaration);
    }

    private bool IsInOwnerModule(INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(OutermostContainer(type), owner);

    /// <summary>
    /// The types whose declarations the load-time code of <paramref name="nestedType"/>
    /// reads, the type itself and the nested types declared with it included.
    /// </summary>
    private IEnumerable<INamedTypeSymbol> LoadTimeDependencies(INamedTypeSymbol nestedType) =>
        DeclaredWith(nestedType)
            .SelectMany(type =>
                BaseTypeOf(type).Concat(StaticInitializationCode(type).SelectMany(ReferencedTypes))
            );

    private IEnumerable<INamedTypeSymbol> ReferencedTypes(SyntaxNode code)
    {
        var model = compilation.GetSemanticModel(code.SyntaxTree);

        return code.DescendantNodesAndSelf(IsEvaluatedAtLoadTime)
            .OfType<ExpressionSyntax>()
            .Select(expression => model.GetSymbolInfo(expression).Symbol)
            .Select(DeclaringType)
            .OfType<INamedTypeSymbol>();
    }

    /// <summary>
    /// <paramref name="type"/> and the nested types of its companion namespace, at any
    /// depth: those not declared at module level themselves.
    /// </summary>
    private IEnumerable<INamedTypeSymbol> DeclaredWith(INamedTypeSymbol type) =>
        type.GetTypeMembers()
            .Where(nested => !hoisting.IsModuleLevel(nested))
            .SelectMany(DeclaredWith)
            .Prepend(type);

    private static IEnumerable<INamedTypeSymbol> BaseTypeOf(INamedTypeSymbol type) =>
        type.BaseType is { } baseType ? [baseType.OriginalDefinition] : [];

    private static IEnumerable<SyntaxNode> StaticInitializationCode(INamedTypeSymbol type) =>
        type.GetMembers()
            .Where(member => member.IsStatic && !member.IsImplicitlyDeclared)
            .SelectMany(member => member.DeclaringSyntaxReferences)
            .Select(reference => LoadTimePart(reference.GetSyntax()))
            .OfType<SyntaxNode>();

    private static SyntaxNode? LoadTimePart(SyntaxNode declaration) =>
        declaration switch
        {
            VariableDeclaratorSyntax field => field.Initializer,
            PropertyDeclarationSyntax property => property.Initializer,
            ConstructorDeclarationSyntax constructor => constructor,
            _ => null,
        };

    private static bool IsEvaluatedAtLoadTime(SyntaxNode node) =>
        node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

    private static INamedTypeSymbol? DeclaringType(ISymbol? symbol) =>
        symbol switch
        {
            INamedTypeSymbol type => type.OriginalDefinition,
            { ContainingType: { } containing } => containing.OriginalDefinition,
            _ => null,
        };

    /// <summary>
    /// The module-level declaration <paramref name="type"/> belongs to: itself or the
    /// nearest container declared at module level; <c>null</c> for a type of an owner
    /// block.
    /// </summary>
    private INamedTypeSymbol? NearestModuleLevelType(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (hoisting.IsModuleLevel(current))
                return current;
        }

        return null;
    }

    private static INamedTypeSymbol OutermostContainer(INamedTypeSymbol type)
    {
        var current = type;

        while (current.ContainingType is not null)
            current = current.ContainingType;

        return current;
    }
}
