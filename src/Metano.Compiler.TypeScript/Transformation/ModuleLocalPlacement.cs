using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Decides on which side of its module-level owner a <c>private</c> nested type's
/// module-local declaration goes (#412). A TypeScript class declaration is not hoisted,
/// so whatever a declaration evaluates at load time must already be declared. The owner
/// and its companion namespace (every non-private nested type, at any depth) are one
/// block; a private nested type is declared before that block so the owner's static
/// initializers and module constants can construct it, unless its own load-time code
/// reads the block — then it must follow it.
/// <para>
/// The load-time code of a private type is the <c>extends</c> clause, static field and
/// property initializers, and static constructor bodies of the type itself and of the
/// non-private nested types declared with it. Lambdas and local functions inside them
/// are deferred, so they are not walked. Two load-time paths are not followed either:
/// the bodies of the methods those initializers call, and the instance initialization
/// (field initializers, constructor body) a <c>new X()</c> runs at load time — a
/// dependency reached only through them is not seen, as on main. A real cycle — the
/// owner's load-time code constructs the type and the type's load-time code reads the
/// owner — has no valid order in either direction and keeps the type after the owner.
/// </para>
/// <para>
/// Only the side of the owner is decided here; the order of the private types within
/// each side is their declaration order.
/// </para>
/// </summary>
internal sealed class ModuleLocalPlacement(INamedTypeSymbol owner, Compilation compilation)
{
    private readonly Dictionary<INamedTypeSymbol, bool> _followsOwner = new(
        SymbolEqualityComparer.Default
    );

    private readonly HashSet<INamedTypeSymbol> _deciding = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// True when the load-time code of <paramref name="privateType"/> reads the owner,
    /// a member of its namespace, or another private nested type that must follow it.
    /// </summary>
    public bool MustFollowOwner(INamedTypeSymbol privateType)
    {
        if (_followsOwner.TryGetValue(privateType, out var decided))
            return decided;

        // A load-time cycle among private types says nothing about the owner: the type
        // being decided counts as not following it. The results cached below while a
        // cycle is open are therefore order-dependent, by design.
        if (!_deciding.Add(privateType))
            return false;

        var follows = LoadTimeDependencies(privateType)
            .Any(dependency => DependsOnOwnerBlock(dependency, privateType));

        _deciding.Remove(privateType);
        _followsOwner[privateType] = follows;

        return follows;
    }

    private bool DependsOnOwnerBlock(INamedTypeSymbol dependency, INamedTypeSymbol privateType)
    {
        var declaringPrivateType = NearestPrivateNestedType(dependency);

        if (declaringPrivateType is null)
            return IsInOwnerModule(dependency);

        var declaredWithPrivateType = SymbolEqualityComparer.Default.Equals(
            declaringPrivateType,
            privateType
        );

        return !declaredWithPrivateType
            && IsInOwnerModule(declaringPrivateType)
            && MustFollowOwner(declaringPrivateType);
    }

    private bool IsInOwnerModule(INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(OutermostContainer(type), owner);

    /// <summary>
    /// The types whose declarations the load-time code of <paramref name="privateType"/>
    /// reads, the type itself and the non-private nested types declared with it included.
    /// </summary>
    private IEnumerable<INamedTypeSymbol> LoadTimeDependencies(INamedTypeSymbol privateType) =>
        DeclaredWith(privateType)
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

    private static IEnumerable<INamedTypeSymbol> DeclaredWith(INamedTypeSymbol privateType) =>
        privateType
            .GetTypeMembers()
            .Where(nested => !SymbolHelper.IsContainerPrivateNestedType(nested))
            .SelectMany(DeclaredWith)
            .Prepend(privateType);

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

    private static INamedTypeSymbol? NearestPrivateNestedType(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (SymbolHelper.IsContainerPrivateNestedType(current))
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
