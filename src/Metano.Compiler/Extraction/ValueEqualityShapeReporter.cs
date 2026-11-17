using Metano.Compiler.Diagnostics;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports, for a target that names the synthesized <c>Equals</c>, what the value
/// equality of a struct or record struct cannot keep (spec 013 FR-045). It runs
/// once per extracted type, from <c>IrClassExtractor.Extract</c>, outside the
/// overload groups' cache:
/// <list type="bullet">
///   <item>a plain struct's <c>Equals(S)</c> that replaces the equality only because
///   it implements a transpiled interface's <c>Equals</c>, not
///   <c>System.IEquatable&lt;S&gt;</c>: the target's sets, dictionaries and record
///   fields compare through it, where .NET's <c>EqualityComparer&lt;S&gt;.Default</c>
///   compares the fields (<c>MS0063</c>);</item>
///   <item>each explicit <c>System.IEquatable&lt;T&gt;.Equals</c> the target drops, its
///   name held by the synthesized <c>Equals(object)</c> (two explicit
///   implementations claim it) or by a user method with its parameters
///   (<c>MS0001</c>, a warning).</item>
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
        // The struct definition the keep rule uses (ExplicitImplementationFates):
        // a plain struct or a record struct.
        if (!policy.NamesSynthesizedEquals || type.TypeKind is not TypeKind.Struct)
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
    /// the target drops: calls through the interface reach the member that holds
    /// its name instead.
    /// </summary>
    private static void ReportDroppedEquatables(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    )
    {
        var name = policy.NameOf(IrSynthesizedValueMember.Equals);

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
                    $"'{type.Name}.{method.Name}' is not emitted: its name '{name}' is held by "
                        + $"{HolderOf(type, method, policy)}, which calls through the interface "
                        + "reach instead (spec 013 FR-045).",
                    method.Locations.FirstOrDefault()
                )
            );
        }
    }

    /// <summary>
    /// The member that holds a dropped implementation's name: a user method of the
    /// type with its parameters, else the synthesized <c>Equals(object)</c>.
    /// </summary>
    private static string HolderOf(
        INamedTypeSymbol type,
        IMethodSymbol method,
        OverloadGroupingPolicy policy
    ) =>
        ExplicitImplementationLowering.Resolve(method, policy.Target)?.ClaimingMember
            is IMethodSymbol { MethodKind: MethodKind.Ordinary } claimant
        && SymbolEqualityComparer.Default.Equals(
            claimant.ContainingType.OriginalDefinition,
            type.OriginalDefinition
        )
            ? $"'{type.Name}.{OverloadSignatureText.DisplayOf(claimant)}'"
            : $"the synthesized 'Equals(object)' of '{type.Name}'";
}
