using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The generic interfaces with overload groups a type implements more than
/// once (<c>C : IConv&lt;int&gt;, IConv&lt;string&gt;</c>, spec 013 DECIDED-30,
/// FR-036). Anchoring on the constructed roots would give two implementations
/// one root name, so the type keeps runtime dispatch for every root name of
/// such an interface; the roots are recorded on the class IR
/// (<see cref="IrClassDeclaration.ConstructedRoots"/>) and the type that
/// introduces the second construction is reported with <c>MS0069</c>.
/// </summary>
public static class MultiplyConstructedInterfaces
{
    /// <summary>
    /// Whether <paramref name="type"/> implements more than one construction
    /// of <paramref name="interfaceType"/>'s definition.
    /// </summary>
    internal static bool AreMultiplyConstructed(
        INamedTypeSymbol type,
        INamedTypeSymbol interfaceType
    ) => ConstructionsOf(type, interfaceType.OriginalDefinition).Count >= 2;

    /// <summary>The roots of every interface the type implements more than once.</summary>
    internal static IReadOnlyList<IrConstructedRoot>? RootsOf(
        INamedTypeSymbol type,
        IrExtractionContext context
    )
    {
        var roots = MultiplyConstructedInterfacesOf(type, context.Target)
            .SelectMany(definition => RootsOf(type, definition, context))
            .ToList();

        return roots.Count > 0 ? roots : null;
    }

    /// <summary>
    /// <c>MS0069</c> for each interface <paramref name="type"/> makes multiply
    /// constructed — the ones its base class already implemented more than
    /// once were reported there — at the type.
    /// </summary>
    public static IReadOnlyList<MetanoDiagnostic> DiagnosticsOf(
        INamedTypeSymbol type,
        TargetLanguage? target
    ) =>
        MultiplyConstructedInterfacesOf(type, target)
            .Where(definition =>
                type.BaseType is not { } baseType || ConstructionsOf(baseType, definition).Count < 2
            )
            .Select(definition => DiagnosticOf(type, definition, target))
            .ToList();

    private static MetanoDiagnostic DiagnosticOf(
        INamedTypeSymbol type,
        INamedTypeSymbol definition,
        TargetLanguage? target
    )
    {
        var names = string.Join(", ", GroupNamesOf(definition, target).Select(name => $"'{name}'"));

        var message =
            $"'{type.Name}' implements '{DisplayOf(definition)}' more than once; "
            + $"calls to {names} keep runtime dispatch (spec 013 FR-036)."
            + IndistinguishableNote(ConstructionsOf(type, definition), target);

        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.MultiplyConstructedOverloadGroup,
            message,
            type.Locations.FirstOrDefault()
        );
    }

    /// <summary>
    /// The first pair of constructions whose type arguments lower to the same
    /// shape (<c>IConv&lt;List&lt;int&gt;&gt;</c> and <c>IConv&lt;int[]&gt;</c>,
    /// both arrays): a runtime check cannot tell them apart, so the first
    /// branch wins, as in the dispatcher.
    /// </summary>
    private static string IndistinguishableNote(
        IReadOnlyList<INamedTypeSymbol> constructions,
        TargetLanguage? target
    )
    {
        var shapes = constructions
            .Select(construction =>
                construction
                    .TypeArguments.Select(argument =>
                        IrTypeRefMapper.Map(argument, originResolver: null, target)
                    )
                    .ToList()
            )
            .ToList();

        for (var i = 0; i < constructions.Count; i++)
        {
            for (var j = i + 1; j < constructions.Count; j++)
            {
                if (shapes[i].SequenceEqual(shapes[j]))
                {
                    return $" The constructions '{DisplayOf(constructions[i])}' and "
                        + $"'{DisplayOf(constructions[j])}' cannot be told apart at run "
                        + "time; the first branch wins.";
                }
            }
        }

        return "";
    }

    private static string DisplayOf(INamedTypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static IEnumerable<IrConstructedRoot> RootsOf(
        INamedTypeSymbol type,
        INamedTypeSymbol definition,
        IrExtractionContext context
    )
    {
        var constructions = ConstructionsOf(type, definition);

        var isInherited =
            type.BaseType is { } baseType && ConstructionsOf(baseType, definition).Count > 0;

        foreach (var name in GroupNamesOf(definition, context.Target))
        {
            foreach (
                var member in MethodOverloadGroups.InterfaceGroupOf(
                    definition,
                    name,
                    context.Target
                )
            )
            {
                yield return new IrConstructedRoot(
                    MethodOverloadGroups.RefOf(member, context.Target),
                    constructions
                        .Select(construction => SignatureIn(construction, member, context))
                        .ToList(),
                    isInherited
                );
            }
        }
    }

    private static IrMethodDeclaration SignatureIn(
        INamedTypeSymbol construction,
        IMethodSymbol member,
        IrExtractionContext context
    )
    {
        var constructed = construction
            .GetMembers(member.Name)
            .OfType<IMethodSymbol>()
            .First(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, member)
            );

        return IrMethodExtractor.ExtractForeignSignature(constructed, context);
    }

    /// <summary>
    /// The transpiled interface definitions with an overload group that
    /// <paramref name="type"/> implements more than once, in the order of its
    /// interfaces.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> MultiplyConstructedInterfacesOf(
        INamedTypeSymbol type,
        TargetLanguage? target
    )
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return [];

        return type
            .AllInterfaces.Select(implemented => implemented.OriginalDefinition)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .Where(definition => IsMultiplyConstructedGroupInterface(type, definition, target));
    }

    private static bool IsMultiplyConstructedGroupInterface(
        INamedTypeSymbol type,
        INamedTypeSymbol definition,
        TargetLanguage? target
    ) =>
        MethodOverloadGroups.IsTranspiledInterface(definition, target)
        && GroupNamesOf(definition, target).Count > 0
        && ConstructionsOf(type, definition).Count >= 2;

    private static IReadOnlyList<INamedTypeSymbol> ConstructionsOf(
        INamedTypeSymbol type,
        INamedTypeSymbol definition
    ) =>
        type
            .AllInterfaces.Where(implemented =>
                SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, definition)
            )
            .ToList();

    /// <summary>The names of the interface's overload groups, in declaration order.</summary>
    private static IReadOnlyList<string> GroupNamesOf(
        INamedTypeSymbol definition,
        TargetLanguage? target
    ) =>
        definition
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Where(name =>
                MethodOverloadGroups.InterfaceGroupOf(definition, name, target).Count >= 2
            )
            .ToList();
}
