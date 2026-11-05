using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Every C# type the descriptors of a context's plan name: each claimed
/// entry's own type (root, closure and polymorphic-variant entries alike)
/// and the wire property types of its object contracts, recursing through
/// the types a container descriptor nests — nullable inner, array / set
/// element, dictionary key and value. The recursion reads
/// <see cref="JsonTypeClassifier.ChildTypesOf"/>, the same accessors
/// <see cref="JsonTypeDescriptorBuilder"/> builds nested descriptors from,
/// so the walk can never name a type the emitted descriptors do not.
/// A <c>ref</c> descriptor's target needs no recursion here: the contract
/// closure makes it an entry of its own, whose type and members are walked.
/// </summary>
internal static class JsonDescriptorTypes
{
    public static IReadOnlyCollection<ITypeSymbol> Of(JsonContextPlan plan)
    {
        var types = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var entry in plan.Entries)
        {
            Collect(entry.Type, types);

            if (entry.Kind == JsonPropertyKind.Ref)
                foreach (
                    var property in plan.Properties.WirePropertiesOf((INamedTypeSymbol)entry.Type)
                )
                    Collect(property.Type, types);
        }

        return types;
    }

    private static void Collect(ITypeSymbol type, HashSet<ITypeSymbol> types)
    {
        if (!types.Add(type))
            return;

        foreach (
            var child in JsonTypeClassifier.ChildTypesOf(type, JsonTypeClassifier.Classify(type))
        )
            Collect(child, types);
    }
}
