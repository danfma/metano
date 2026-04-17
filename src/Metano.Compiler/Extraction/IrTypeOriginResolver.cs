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
