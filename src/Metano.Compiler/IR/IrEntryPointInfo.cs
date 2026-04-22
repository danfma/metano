using Microsoft.CodeAnalysis;

namespace Metano.Compiler.IR;

/// <summary>
/// Describes the compilation's top-level-statement entry point when
/// present. Populated by the frontend only when
/// <c>[assembly: TranspileAssembly]</c> is set, the compilation contains
/// <c>GlobalStatementSyntax</c>, Roslyn reports an entry point, and the
/// containing type is not opted out via <c>[ExportedAsModule]</c>.
/// Target backends check for <c>null</c> to decide whether to route the
/// entry-point body through top-level-statement emission instead of
/// regular class emission.
/// <para>
/// Both members are Roslyn escape hatches — the synthetic <c>Program</c>
/// routing still calls into semantic-model-backed bridges that need the
/// original <see cref="IMethodSymbol"/> to walk the entry-point body.
/// </para>
/// </summary>
/// <param name="Method">Compiler-synthesized entry-point method
/// (<see cref="Microsoft.CodeAnalysis.CSharp.CSharpCompilation.GetEntryPoint"/>).</param>
/// <param name="ContainingType">The synthesized containing type
/// (usually <c>Program</c>) that targets route through top-level-
/// statement emission.</param>
public sealed record IrEntryPointInfo(IMethodSymbol Method, INamedTypeSymbol ContainingType);
