using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The barrel hoists the lazy static constructor needs (spec 001 FR-066, #681).
/// A class whose type initializer (its static constructor body, or a static field
/// or property initializer, which run inside <c>#$init</c>) names a class declared
/// in another module that <c>extends</c> it, directly or transitively, forms an
/// import cycle: the base module imports the derived one, whose <c>extends</c>
/// clause reads the base class at load. The cycle loads only when the derived
/// module is evaluated first, so the barrel lists it first
/// (<see cref="BarrelFileGenerator"/>).
/// </summary>
public static class InitializerCycleHoists
{
    /// <summary>
    /// One hoist per (base module, derived module) pair, including intermediate
    /// bases along the referenced descendant's inheritance chain, in the order of
    /// <paramref name="moduleOf"/>'s types. <paramref name="moduleOf"/> maps every
    /// emitted type to its module path (relative, without extension).
    /// </summary>
    public static IReadOnlyList<BarrelHoist> Collect(
        Compilation compilation,
        IReadOnlyDictionary<INamedTypeSymbol, string> moduleOf
    )
    {
        var hoists = new List<BarrelHoist>();

        foreach (var (type, module) in moduleOf)
        {
            if (!TypeInitializerLowering.RunsLazily(new(type, compilation)))
                continue;

            foreach (var derived in DerivedTypesNamedByInitializer(type, compilation))
            {
                if (
                    !moduleOf.TryGetValue(derived, out var derivedModule)
                    || derivedModule == module
                )
                    continue;

                hoists.Add(new BarrelHoist(module, derivedModule));

                // Enter the cycle through its leaf, not an intermediate extends
                // clause that can read that leaf's uninitialized base binding.
                for (
                    var ancestor = derived.BaseType;
                    ancestor is not null;
                    ancestor = ancestor.BaseType
                )
                {
                    if (
                        SymbolEqualityComparer.Default.Equals(
                            ancestor.OriginalDefinition,
                            type.OriginalDefinition
                        )
                    )
                        break;

                    if (
                        moduleOf.TryGetValue(ancestor.OriginalDefinition, out var ancestorModule)
                        && ancestorModule != derivedModule
                    )
                        hoists.Add(new BarrelHoist(ancestorModule, derivedModule));
                }
            }
        }

        return hoists.Distinct().ToList();
    }

    internal static IEnumerable<INamedTypeSymbol> DerivedTypesNamedByInitializer(
        INamedTypeSymbol type,
        Compilation compilation
    ) =>
        InitializerSyntax(type)
            .SelectMany(node => NamedTypes(node, compilation))
            .Where(named => DerivesFrom(named, type))
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    internal static IEnumerable<SyntaxNode> InitializerSyntax(INamedTypeSymbol type)
    {
        var staticConstructor = IrStaticConstructorExtractor.DeclaredBy(type);

        var staticMembers = type.GetMembers()
            .Where(member =>
                member.IsStatic && member is IFieldSymbol { IsConst: false } or IPropertySymbol
            )
            .Append(staticConstructor);

        return staticMembers
            .OfType<ISymbol>()
            .SelectMany(member => member.DeclaringSyntaxReferences)
            .Select(reference => reference.GetSyntax())
            .Select(InitializingPart)
            .OfType<SyntaxNode>();
    }

    private static SyntaxNode? InitializingPart(SyntaxNode declaration) =>
        declaration switch
        {
            VariableDeclaratorSyntax variable => variable.Initializer,
            PropertyDeclarationSyntax property => property.Initializer,
            ConstructorDeclarationSyntax constructor => (SyntaxNode?)constructor.Body
                ?? constructor.ExpressionBody,
            _ => null,
        };

    private static IEnumerable<INamedTypeSymbol> NamedTypes(
        SyntaxNode node,
        Compilation compilation
    )
    {
        var model = compilation.GetSemanticModel(node.SyntaxTree);

        return node.DescendantNodesAndSelf()
            .OfType<SimpleNameSyntax>()
            .Select(name => model.GetSymbolInfo(name).Symbol)
            .OfType<INamedTypeSymbol>()
            .Select(named => named.OriginalDefinition);
    }

    private static bool DerivesFrom(INamedTypeSymbol candidate, INamedTypeSymbol type)
    {
        for (var current = candidate.BaseType; current is not null; current = current.BaseType)
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    type.OriginalDefinition
                )
            )
                return true;
        }

        return false;
    }
}
