using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports, for a target that names the synthesized <c>Equals</c>, what a
/// struct's value equality cannot keep (spec 013 FR-045), once per extracted type
/// and outside every cached computation:
/// <list type="bullet">
///   <item>an <c>Equals(S)</c> that replaces the equality only because it implements
///   a transpiled interface's <c>Equals</c>, not <c>System.IEquatable&lt;S&gt;</c>: the
///   target's sets, dictionaries and record fields compare through it, where .NET's
///   <c>EqualityComparer&lt;S&gt;.Default</c> compares the fields (<c>MS0063</c>);</item>
///   <item>explicit <c>System.IEquatable&lt;T&gt;.Equals</c> implementations the target
///   drops, because two of them claim one name (<c>MS0001</c>, a warning).</item>
/// </list>
/// It reads the type's <see cref="IrValueEqualityShape"/> first, and the symbols only
/// to find the method a message names.
/// </summary>
internal static class ValueEqualityShapeReporter
{
    /// <summary>Reports <paramref name="type"/>'s value-equality findings.</summary>
    public static void Report(
        INamedTypeSymbol type,
        IrValueEqualityShape shape,
        OverloadGroupingPolicy policy
    )
    {
        if (!policy.NamesSynthesizedEquals || shape.Kind is not IrValueEqualityKind.Struct)
            return;

        if (shape.ReplacesEquals)
            ReportTranspiledOnlyReplacement(type, policy);

        ReportDroppedEquatables(type, policy);
    }

    private static void ReportTranspiledOnlyReplacement(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    )
    {
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

    /// <summary>
    /// <c>MS0001</c> (a warning) at each explicit <c>System.IEquatable&lt;T&gt;.Equals</c>
    /// the target drops: two of them claim the name <c>equals</c>, and neither can be
    /// declared, so calls through the interface reach the struct's other equality.
    /// </summary>
    private static void ReportDroppedEquatables(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    )
    {
        var dropped = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method =>
                method.ExplicitInterfaceImplementations.Any(implemented =>
                    EquatableInterfaces.IsEquatable(implemented.ContainingType)
                )
                && !method.HasIgnore(policy.Target)
                && ExplicitImplementationFates.IsDropped(method, policy)
            );

        foreach (var method in dropped)
        {
            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.UnsupportedFeature,
                    $"'{type.Name}.{method.Name}' is not emitted: another member of "
                        + $"'{type.Name}' already declares 'equals', so calls through the "
                        + "interface reach that member instead (spec 013 FR-045).",
                    method.Locations.FirstOrDefault()
                )
            );
        }
    }
}
