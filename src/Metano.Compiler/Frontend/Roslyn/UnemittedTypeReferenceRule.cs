using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// <c>MS0002</c> (#770): under <c>[assembly: TranspileAssembly]</c>, a reference from
/// emitted code to a type of the same assembly that is still not emitted — after
/// <see cref="InternalTypeReachability"/> has pulled in every reachable internal type —
/// would name a symbol the output never declares (a ReferenceError at run time). That is
/// a <c>file</c>-local type today. <c>[Ignore]</c> keeps its own <c>MS0013</c>, and
/// <c>[External]</c> / <c>[JsCallable]</c> types are ambient by design.
/// </summary>
internal sealed class UnemittedTypeReferenceRule(
    IAssemblySymbol assembly,
    TargetLanguage target,
    List<MetanoDiagnostic> diagnostics
) : ITranspiledReferenceRule
{
    private readonly IAssemblySymbol _assembly = assembly;
    private readonly TargetLanguage _target = target;
    private readonly List<MetanoDiagnostic> _diagnostics = diagnostics;
    private readonly HashSet<Location> _reportedLocations = [];

    /// <summary>The rule when the assembly transpiles assembly-wide; none otherwise.</summary>
    public static IEnumerable<ITranspiledReferenceRule> For(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        List<MetanoDiagnostic> diagnostics
    ) =>
        assemblyWideTranspile
            ? [new UnemittedTypeReferenceRule(compilation.Assembly, target, diagnostics)]
            : [];

    public void CheckType(INamedTypeSymbol type, Location location)
    {
        if (IsUnemitted(type.OriginalDefinition) && _reportedLocations.Add(location))
            Report(type, location);
    }

    public void CheckMember(ISymbol member, Location location) { }

    private bool IsUnemitted(INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _assembly)
        && type is { IsImplicitlyDeclared: false, IsAnonymousType: false }
        && !string.IsNullOrEmpty(type.Name)
        && type.Locations.Any(location => location.IsInSource)
        && !SymbolHelper.HasIgnore(type, _target)
        && !SymbolHelper.HasExternal(type)
        && !SymbolHelper.HasJsCallable(type)
        && !SymbolHelper.IsTranspilable(type, assemblyWideTranspile: true, _assembly);

    private void Report(INamedTypeSymbol type, Location location) =>
        _diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnresolvedType,
                $"Transpiled code references '{type.Name}', which is not transpiled, so the "
                    + "output names a symbol it never declares. Make the type transpilable "
                    + "(a file-local type never is) or remove the reference.",
                location
            )
        );
}
