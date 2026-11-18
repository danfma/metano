using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The root an anchored member reuses the names of (spec 013 FR-025, FR-041):
/// a declared method, or a synthesized member of an emitted base class.
/// </summary>
internal abstract record AnchorRoot;

/// <summary>A declared root: the first declaration of an override chain, or an interface member.</summary>
internal sealed record DeclaredRoot(IMethodSymbol Method) : AnchorRoot;

/// <summary>The synthesized <paramref name="Kind"/> member a base class inserted into a group.</summary>
internal sealed record SynthesizedRoot(INamedTypeSymbol Owner, IrSynthesizedValueMember Kind)
    : AnchorRoot;

/// <summary>
/// How a group member is anchored on a root it does not declare (spec 013
/// FR-025): one shape for declared and synthesized members.
/// </summary>
/// <param name="Root">The root whose names it reuses.</param>
/// <param name="Kind">Override or implementation.</param>
/// <param name="FurtherRoots">Other dispatched interface members the method
/// implements, in the order of the type's interfaces.</param>
/// <param name="ReimplementedRoots">The roots an emitted base class already
/// implements, which the member re-implements.</param>
internal sealed record MemberAnchor(
    AnchorRoot Root,
    IrOverloadAnchorKind Kind,
    IReadOnlyList<IMethodSymbol> FurtherRoots,
    IReadOnlyList<IMethodSymbol>? ReimplementedRoots = null
)
{
    /// <summary>The declared root method; <c>null</c> for a synthesized root.</summary>
    public IMethodSymbol? RootMethod => (Root as DeclaredRoot)?.Method;
}
