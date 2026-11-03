using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Reports <c>MS0034</c> for every invalid <c>[JsonFactory]</c> in the transpiled
/// types (spec 009 FR-022, FR-023). It runs over the types, not over the contexts, so
/// a factory is checked whether or not a <c>JsonSerializerContext</c> reaches its type.
/// </summary>
internal sealed class JsonBrandedFactoryValidator(
    IReadOnlyList<INamedTypeSymbol> transpilableTypes,
    Action<MetanoDiagnostic> reportDiagnostic
)
{
    public void Validate()
    {
        foreach (var type in transpilableTypes.SelectMany(WithNestedTypes))
        {
            foreach (var diagnostic in JsonBrandedFactoryResolver.DiagnosticsFor(type))
            {
                reportDiagnostic(diagnostic);
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> WithNestedTypes(INamedTypeSymbol type) =>
        type.GetTypeMembers().SelectMany(WithNestedTypes).Prepend(type);
}
