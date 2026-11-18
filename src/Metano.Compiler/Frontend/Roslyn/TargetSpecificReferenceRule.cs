using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// <c>MS0051</c> (spec 011 FR-033): a reference from emitted code to an interop API
/// bound to another target language (<see cref="TargetSpecificInterop"/>), such as
/// <c>Js.Code</c> in a Dart emission. The marker is read on the symbol and its
/// containing types, so <c>using static Metano.Interop.Js; Code(…)</c> is caught too.
/// One reference is reported once: the type name and the member name of
/// <c>Js.Code</c> both widen to the whole qualified access.
/// </summary>
internal sealed class TargetSpecificReferenceRule(
    TargetLanguage target,
    List<MetanoDiagnostic> diagnostics
) : ITranspiledReferenceRule
{
    private readonly TargetLanguage _target = target;
    private readonly List<MetanoDiagnostic> _diagnostics = diagnostics;
    private readonly HashSet<Location> _reportedLocations = [];

    public void CheckType(INamedTypeSymbol type, Location location) => Check(type, location);

    public void CheckMember(ISymbol member, Location location) => Check(member, location);

    private void Check(ISymbol symbol, Location location)
    {
        if (TargetSpecificInterop.LanguageOf(symbol) is not { } language || language == _target)
            return;

        var reference = QualifiedReferenceAt(location);

        if (_reportedLocations.Add(reference))
            Report(symbol, language, reference);
    }

    /// <summary>
    /// The whole qualified access a name or receiver belongs to (<c>Js.Code</c> for
    /// both <c>Js</c> and <c>Code</c>), so one reference reports once.
    /// </summary>
    private static Location QualifiedReferenceAt(Location location)
    {
        if (location.SourceTree?.GetRoot().FindNode(location.SourceSpan) is not { } node)
            return location;

        while (node.Parent is MemberAccessExpressionSyntax or QualifiedNameSyntax)
            node = node.Parent;

        return node.GetLocation();
    }

    private void Report(ISymbol symbol, TargetLanguage language, Location location) =>
        _diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.TargetSpecificInterop,
                $"'{symbol.ToDisplayString()}' exists only for {language}; this {_target} emission "
                    + "cannot use it. Mark the containing member or type "
                    + $"[Ignore(TargetLanguage.{_target})] to keep it out of this target.",
                location
            )
        );
}
