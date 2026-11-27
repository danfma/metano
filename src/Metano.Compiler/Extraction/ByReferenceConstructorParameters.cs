using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports the <c>out</c> and <c>ref</c> parameters of constructors (#835).
/// A method lowers its <c>out</c> parameters into its result, but a
/// constructor's result is the instance, so the parameter would be emitted
/// by value and the constructor's writes would never reach the caller.
/// </summary>
internal static class ByReferenceConstructorParameters
{
    /// <summary>
    /// Reports MS0001 (error) at each <c>out</c> or <c>ref</c> parameter of
    /// <paramref name="constructors"/>.
    /// </summary>
    public static void Report(IEnumerable<IMethodSymbol> constructors)
    {
        foreach (var parameter in constructors.SelectMany(c => c.Parameters))
        {
            if (parameter.RefKind is not (RefKind.Out or RefKind.Ref))
                continue;

            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.UnsupportedFeature,
                    $"Constructor parameter '{parameter.Name}' of "
                        + $"'{parameter.ContainingSymbol.ToDisplayString()}' is "
                        + $"'{RefKindKeyword(parameter.RefKind)}', which a constructor cannot "
                        + "lower: it would be passed by value, so the constructor's writes would "
                        + "not reach the caller.",
                    parameter.Locations.FirstOrDefault()
                )
            );
        }
    }

    private static string RefKindKeyword(RefKind refKind) => refKind is RefKind.Out ? "out" : "ref";
}
