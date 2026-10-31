using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Resolves the cross-assembly <see cref="IrTypeOrigin"/> for a named Roslyn type, or
/// returns <c>null</c> when the type does not come from another transpilable package.
/// <para>
/// Supplied by the hosting backend (the TypeScript target provides one built from its
/// <c>CrossAssemblyTypeMap</c>) so that the shared IR extractors stay target-agnostic
/// while still producing origin-aware type references.
/// </para>
/// </summary>
public delegate IrTypeOrigin? IrTypeOriginResolver(INamedTypeSymbol type);

/// <summary>
/// Origin lookups shared by the type-position and value-position extractors.
/// </summary>
public static class IrTypeOriginResolverExtensions
{
    /// <summary>
    /// Resolves the origin of <paramref name="type"/> through its outermost containing
    /// type: a nested type is reached through its container's import, so the container
    /// is the symbol the producing package exports. Null when
    /// <paramref name="resolver"/> is null or the type is not cross-package.
    /// </summary>
    public static IrTypeOrigin? ResolveThroughOutermost(
        this IrTypeOriginResolver? resolver,
        INamedTypeSymbol type
    )
    {
        var outermost = type;

        while (outermost.ContainingType is { } container)
            outermost = container;

        return resolver?.Invoke(outermost);
    }
}
