// Pulled in so the `<see cref="ISourceFrontend"/>` and
// `<see cref="SymbolHelper.GetStableFullName"/>` references below resolve
// from this nested namespace without needing fully-qualified names.
using Metano.Compiler;
using Metano.Compiler.Diagnostics;

namespace Metano.Compiler.IR;

/// <summary>
/// Target-agnostic result of a source frontend's extraction pass. Carries
/// every piece of data a backend needs to emit its target without going
/// back to the source language: the <see cref="Modules"/> it should emit
/// (one per output file group), the referenced modules it can import from,
/// and the registry tables that drive cross-package resolution and BCL
/// mapping.
/// <para>
/// A frontend implementing <see cref="ISourceFrontend"/> builds an
/// <see cref="IrCompilation"/> once per transpile run. All backend-specific
/// state (naming policy, package managers, emit flags) stays inside the
/// backend; the compilation record is source-language-agnostic and
/// target-language-agnostic.
/// </para>
/// </summary>
/// <param name="AssemblyName">Logical name of the source unit being
/// transpiled (e.g., the C# assembly name).</param>
/// <param name="PackageName">Declared <c>[assembly: EmitPackage]</c> value
/// for the target, if any. Backends that publish to a package registry
/// (npm, pub) use this as the package identity.</param>
/// <param name="AssemblyWideTranspile">When <c>true</c>, the source unit
/// declared <c>[TranspileAssembly]</c> — public types without
/// <c>[Transpile]</c> should be treated as transpilable by default.</param>
/// <param name="Modules">Every module the backend should emit. Grouping
/// (e.g., <c>[EmitInFile]</c>) is already resolved by the frontend.</param>
/// <param name="ReferencedModules">Modules discovered in referenced
/// assemblies that declared <c>[TranspileAssembly]</c> + <c>[EmitPackage]</c>.
/// The backend reads these to populate cross-package imports without
/// emitting their content.</param>
/// <param name="CrossAssemblyOrigins">Dictionary keyed by the stable full
/// name produced during extraction (see
/// <see cref="SymbolHelper.GetStableFullName"/>) giving the
/// <see cref="IrTypeOrigin"/> for every referenced type that belongs to a
/// transpilable assembly. Consumers should use this registry only when
/// they already have that stable key available from frontend-produced
/// metadata; <see cref="IrNamedTypeRef"/> alone is not a direct lookup
/// key — the origin travels via <see cref="IrNamedTypeRef.Origin"/> when
/// available.</param>
/// <param name="ExternalImports">Dictionary keyed by type name (both source
/// and emitted) giving the <see cref="IrExternalImport"/> to emit for
/// <c>[Import]</c>-annotated types.</param>
/// <param name="BclExports">Dictionary keyed by BCL identifier (e.g.,
/// <c>"decimal"</c>) giving the <see cref="IrBclExport"/> the target should
/// substitute.</param>
/// <param name="AssembliesNeedingEmitPackage">Set of assembly names that
/// had <c>[TranspileAssembly]</c> but no <c>[EmitPackage]</c> for the
/// active target. A backend reports <c>MS0007</c> for any cross-package
/// reference to one of these.</param>
/// <param name="Diagnostics">Diagnostics raised during extraction (e.g.,
/// conflicts in <c>[EmitInFile]</c> groupings, malformed
/// <c>[Import]</c> attributes).</param>
public sealed record IrCompilation(
    string AssemblyName,
    string? PackageName,
    bool AssemblyWideTranspile,
    IReadOnlyList<IrModule> Modules,
    IReadOnlyList<IrModule> ReferencedModules,
    IReadOnlyDictionary<string, IrTypeOrigin> CrossAssemblyOrigins,
    IReadOnlyDictionary<string, IrExternalImport> ExternalImports,
    IReadOnlyDictionary<string, IrBclExport> BclExports,
    IReadOnlySet<string> AssembliesNeedingEmitPackage,
    IReadOnlyList<MetanoDiagnostic> Diagnostics
);
