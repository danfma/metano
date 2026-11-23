using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Diagnostics for C# enumeration contracts the TypeScript output cannot carry over
/// to the JavaScript iteration protocol (see <see cref="IrToTsIterationProtocolBridge"/>).
/// </summary>
internal static class IteratorProtocolDiagnostics
{
    /// <summary>
    /// Reports MS0001 when <paramref name="type"/> takes on <c>IEnumerator</c> but
    /// declares its own <c>Next</c> (<see cref="IrToTsIterationProtocolBridge.DeclaresConflictingNext"/>),
    /// whether it lowers to a class (a <c>next</c> member) or to a <c>[PlainObject]</c>
    /// interface (a <c>next</c> helper function). Either way it is emitted without the
    /// <c>Iterator&lt;T&gt;</c> contract or a synthesized <c>next()</c>, so it does not
    /// follow the JavaScript iterator protocol: <c>for…of</c>, spread and a
    /// <c>GetEnumerator()</c> that returns it cannot consume it.
    /// </summary>
    public static void ReportConflictingNext(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        Action<MetanoDiagnostic> report
    )
    {
        if (!IrToTsIterationProtocolBridge.DeclaresConflictingNext(ir))
            return;

        report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{type.Name}' implements IEnumerator but declares its own 'next', which "
                    + "the JavaScript iterator protocol reserves: it is not emitted as an "
                    + "Iterator, so it cannot be consumed as one.",
                type.Locations.FirstOrDefault()
            )
        );
    }

    /// <summary>
    /// Reports MS0001 when <paramref name="type"/> inherits <c>IEnumerator</c> from a
    /// base class, whose emitted <c>next()</c> is the JavaScript iterator step, and
    /// declares its own member emitted as <c>next</c>, which overrides that step: the
    /// type no longer follows the iterator protocol, and its <c>next</c> need not
    /// match the inherited signature (#350).
    /// </summary>
    public static void ReportNextOverridingInheritedStep(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        Action<MetanoDiagnostic> report
    )
    {
        if (ir.Semantics.Enumerator is not null || !IrToTsIterationProtocolBridge.DeclaresNext(ir))
            return;

        if (EnumeratorBaseOf(type) is not { } enumeratorBase)
            return;

        report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{type.Name}' inherits IEnumerator from '{enumeratorBase.Name}' but declares "
                    + "its own 'next', which overrides the inherited JavaScript iterator step: "
                    + "it cannot be consumed as an Iterator.",
                type.Locations.FirstOrDefault()
            )
        );
    }

    private static INamedTypeSymbol? EnumeratorBaseOf(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.AllInterfaces.Any(IsEnumeratorContract))
                return current;
        }

        return null;
    }

    private static bool IsEnumeratorContract(INamedTypeSymbol contract) =>
        contract.SpecialType is SpecialType.System_Collections_IEnumerator;
}
