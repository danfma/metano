using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Builds the <see cref="IrTypeKey"/> of a Roslyn type and finds the type a
/// key names, so the core can record an identity a target resolves without
/// carrying symbols in the IR.
/// </summary>
public static class IrTypeKeys
{
    /// <summary>The key of <paramref name="type"/>'s definition.</summary>
    public static IrTypeKey Of(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;

        return new IrTypeKey(definition.ContainingAssembly?.Name ?? "", MetadataNameOf(definition));
    }

    /// <summary>
    /// The type <paramref name="key"/> names among the assemblies
    /// <paramref name="compilation"/> sees — its own and its references';
    /// <c>null</c> when none declares it.
    /// </summary>
    public static INamedTypeSymbol? Find(IrTypeKey key, Compilation compilation) =>
        AssembliesOf(compilation)
            .FirstOrDefault(assembly => assembly.Name == key.AssemblyName)
            ?.GetTypeByMetadataName(key.MetadataName);

    private static IEnumerable<IAssemblySymbol> AssembliesOf(Compilation compilation) =>
        compilation.SourceModule.ReferencedAssemblySymbols.Prepend(compilation.Assembly);

    /// <summary>
    /// The name <see cref="IAssemblySymbol.GetTypeByMetadataName"/> takes:
    /// namespace-qualified, with <c>+</c> before a nested type and the generic
    /// arity suffix (<c>Shop.Outer+Box`1</c>).
    /// </summary>
    private static string MetadataNameOf(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } container)
            return $"{MetadataNameOf(container)}+{type.MetadataName}";

        return type.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? $"{containingNamespace.ToDisplayString()}.{type.MetadataName}"
            : type.MetadataName;
    }
}
