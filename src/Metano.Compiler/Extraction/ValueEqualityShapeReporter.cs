using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports, for a target that names the synthesized <c>Equals</c>, a struct whose
/// <c>Equals(S)</c> replaces the value equality only because it implements a
/// transpiled interface's <c>Equals</c>, not <c>System.IEquatable&lt;S&gt;</c>
/// (spec 013 FR-045): the target's sets, dictionaries and record fields compare
/// through it, where .NET's <c>EqualityComparer&lt;S&gt;.Default</c> compares the
/// fields. An <c>MS0063</c> warning at the method.
/// </summary>
internal static class ValueEqualityShapeReporter
{
    /// <summary>Reports <paramref name="type"/>'s transpiled-only replacement, if any.</summary>
    public static void Report(INamedTypeSymbol type, OverloadGroupingPolicy policy)
    {
        if (policy.NameOf(IrSynthesizedValueMember.Equals) is null)
            return;

        if (ValueEqualityShapes.TranspiledOnlyEqualsOf(type, policy) is not { } method)
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousRuntimeDispatch,
                $"'{type.Name}.{OverloadSignatureText.DisplayOf(method)}' replaces the value "
                    + $"equality of '{type.Name}' because it implements a transpiled "
                    + "interface's 'Equals': sets, dictionaries and record fields compare "
                    + $"through it, where .NET compares the fields unless '{type.Name}' "
                    + $"implements 'IEquatable<{type.Name}>' (spec 013 FR-045).",
                method.Locations.FirstOrDefault()
            )
        );
    }
}
