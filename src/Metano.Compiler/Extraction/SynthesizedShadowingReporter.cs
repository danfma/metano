using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Reports the overloads a group's synthesized member shadows
/// (<see cref="SynthesizedShadowing"/>, spec 013 FR-042): nothing when the type
/// replaces <c>Equals</c> itself, whose own equality the dispatcher reaches;
/// <c>MS0001</c> as an error when the overload implements a transpiled interface
/// member, whose callers reach it through the shared dispatcher name; <c>MS0063</c>
/// otherwise.
/// </summary>
internal static class SynthesizedShadowingReporter
{
    /// <summary>Reports each of <paramref name="shadowed"/> once.</summary>
    public static void Report(
        INamedTypeSymbol type,
        MethodOverloadGroup group,
        IReadOnlySet<DeclaredMember> shadowed,
        OverloadGroupingPolicy policy
    )
    {
        if (group.Synthesized is not { } synthesized)
            return;

        foreach (var member in shadowed)
            ReportShadowed(type, member, synthesized, policy);
    }

    private static void ReportShadowed(
        INamedTypeSymbol type,
        DeclaredMember member,
        SynthesizedMember synthesized,
        OverloadGroupingPolicy policy
    )
    {
        if (IsCoveredByReplacement(type, synthesized, policy))
            return;

        var method = member.Symbol;
        var shared = policy.NameOf(synthesized.Kind);
        var signature = OverloadSignatureText.DisplayOf(method);

        if (ImplementedInterfaceMemberOf(type, method, policy) is { } implemented)
        {
            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.UnsupportedFeature,
                    $"'{type.Name}.{signature}' implements "
                        + $"'{implemented.ContainingType.Name}.{implemented.Name}', whose callers "
                        + $"reach it through "
                        + $"'{shared}', but a runtime dispatcher cannot tell it from the "
                        + $"synthesized '{SynthesizedText(synthesized.Kind)}' of '{type.Name}', "
                        + "which keeps that name; those callers would reach the synthesized "
                        + "member. Give the overload a parameter of a type the dispatcher can "
                        + "tell apart, or rename it (spec 013 FR-042).",
                    method.Locations.FirstOrDefault()
                )
            );

            return;
        }

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousRuntimeDispatch,
                $"Overload '{signature}' cannot be told apart at runtime from the synthesized "
                    + $"'{SynthesizedText(synthesized.Kind)}' of '{type.Name}', which shares "
                    + $"its name '{shared}': its C# calls reach it directly, but a call "
                    + "through the dispatcher (an interface, object, the runtime collections) "
                    + "reaches the synthesized member (spec 013 FR-042).",
                method.Locations.FirstOrDefault()
            )
        );
    }

    /// <summary>
    /// The member of a transpiled interface <paramref name="method"/> implements,
    /// whose callers reach it through the shared dispatcher name; <c>null</c> when
    /// it implements none. An override needs no such check: it is anchored on its
    /// base's group, which a synthesized member joins too, and its C# calls bind
    /// statically.
    /// </summary>
    private static IMethodSymbol? ImplementedInterfaceMemberOf(
        INamedTypeSymbol type,
        IMethodSymbol method,
        OverloadGroupingPolicy policy
    ) =>
        type
            .AllInterfaces.Where(candidate =>
                EmittedTypes.IsTranspiledInterface(candidate, policy.Target)
            )
            .SelectMany(candidate => candidate.GetMembers().OfType<IMethodSymbol>())
            .FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    type.FindImplementationForInterfaceMember(candidate)?.OriginalDefinition,
                    method.OriginalDefinition
                )
            );

    /// <summary>
    /// The type replaces the synthesized <c>Equals</c> itself (a record's
    /// <c>Equals(R)</c>, a struct's <c>override Equals(object)</c> or
    /// interface-implementing <c>Equals(S)</c>): calls
    /// through the dispatcher reach the user's equality, which owns the
    /// consistency of its overloads.
    /// </summary>
    private static bool IsCoveredByReplacement(
        INamedTypeSymbol type,
        SynthesizedMember synthesized,
        OverloadGroupingPolicy policy
    ) =>
        synthesized.Kind is IrSynthesizedValueMember.Equals
        && ValueEqualityShapes.Of(type, policy).ReplacesEquals;

    private static string SynthesizedText(IrSynthesizedValueMember kind) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => "Equals(object)",
            IrSynthesizedValueMember.HashCode => "GetHashCode()",
            _ => "with copy",
        };
}
