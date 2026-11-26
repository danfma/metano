using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Warns (<c>MS0001</c>) at each <c>[Import]</c> class of the compilation that implements
/// <c>IDisposable</c>, directly or through an interface, when C# <c>using</c> is emitted as a
/// TypeScript <c>using</c> declaration (FR-111 A1.2, decision W11-1a). A resource typed by an
/// interface or type parameter is always declared with <c>using</c>, which calls
/// <c>[Symbol.dispose]()</c>; the hand-written JavaScript class bound by <c>[Import]</c> has
/// none, so its instances throw a <c>TypeError</c> there. A resource typed by the class itself
/// keeps the <c>try</c>/<c>finally</c> lowering and is not affected.
/// </summary>
internal sealed class ImportedDisposableDiagnostics(
    Compilation compilation,
    Action<MetanoDiagnostic> reportDiagnostic
)
{
    private readonly Compilation _compilation = compilation;
    private readonly Action<MetanoDiagnostic> _reportDiagnostic = reportDiagnostic;

    public void Report()
    {
        foreach (var type in CollectTypes(_compilation.Assembly.GlobalNamespace))
        {
            if (IsImportedDisposable(type))
                _reportDiagnostic(Unusable(type));
        }
    }

    private static IEnumerable<INamedTypeSymbol> CollectTypes(INamespaceSymbol ns) =>
        ns.GetTypeMembers()
            .SelectMany(WithNestedTypes)
            .Concat(ns.GetNamespaceMembers().SelectMany(CollectTypes));

    private static IEnumerable<INamedTypeSymbol> WithNestedTypes(INamedTypeSymbol type) =>
        [type, .. type.GetTypeMembers().SelectMany(WithNestedTypes)];

    private static bool IsImportedDisposable(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct
        && type.HasImport()
        && type.AllInterfaces.Any(contract =>
            contract.SpecialType is SpecialType.System_IDisposable
        );

    private static MetanoDiagnostic Unusable(INamedTypeSymbol type) =>
        new(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.UnsupportedFeature,
            $"'{type.Name}' is an [Import] class that implements IDisposable: its JavaScript "
                + "class has no [Symbol.dispose](), so an instance held through IDisposable, "
                + "an interface derived from it or a type parameter throws a TypeError at a "
                + "C# using, which is emitted as a TypeScript using declaration (FR-111). "
                + "Use the class type at those using sites, or set "
                + "MetanoUsingLowering=tryFinally.",
            type.Locations.FirstOrDefault()
        );
}
