using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>The source text a diagnostic is reported at.</summary>
internal static class LocatedSource
{
    /// <summary>
    /// The text under the location of <paramref name="diagnostic"/>; fails
    /// when it carries no source location.
    /// </summary>
    public static string TextOf(MetanoDiagnostic diagnostic)
    {
        if (diagnostic.Location is not { SourceTree: { } tree } location)
        {
            throw new InvalidOperationException(
                $"Diagnostic {diagnostic.Code} has no source location: {diagnostic.Message}"
            );
        }

        return tree.GetText().ToString(location.SourceSpan);
    }
}
