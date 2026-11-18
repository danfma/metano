using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// <c>MS0013</c>: a reference from emitted code to a type or member that is
/// <c>[Ignore]</c>d (.NET-only) for the running target.
/// </summary>
internal sealed class IgnoredReferenceRule(
    TargetLanguage target,
    List<MetanoDiagnostic> diagnostics
) : ITranspiledReferenceRule
{
    private readonly TargetLanguage _target = target;
    private readonly List<MetanoDiagnostic> _diagnostics = diagnostics;
    private readonly HashSet<Location> _reportedLocations = [];

    public void CheckType(INamedTypeSymbol type, Location location)
    {
        if (SymbolHelper.HasIgnore(type, _target) && _reportedLocations.Add(location))
            ReportType(type, location);
    }

    public void CheckMember(ISymbol member, Location location)
    {
        if (SymbolHelper.HasIgnore(member, _target) && _reportedLocations.Add(location))
            ReportMember(member, location);
    }

    private void ReportType(INamedTypeSymbol type, Location location) =>
        _diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.IgnoreReferencedByTranspiledCode,
                $"Transpilable code references '{type.Name}', which is marked [Ignore] "
                    + "(.NET-only). Migrate the type to [External] (ambient TS shape) "
                    + "or remove the dependency from transpiled code.",
                location
            )
        );

    private void ReportMember(ISymbol member, Location location)
    {
        var owner = member.ContainingType?.Name ?? "<global>";

        _diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.IgnoreReferencedByTranspiledCode,
                $"Transpilable code references '{owner}.{member.Name}', which is marked [Ignore] "
                    + "(.NET-only). The member is not emitted; remove the reference or drop "
                    + "[Ignore] from the member.",
                location
            )
        );
    }
}
