using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Reports <c>MS0063</c> for each catch-all overload of a constructor or
/// method dispatcher (see <see cref="CatchAllOverloads"/>), at the overload's
/// declaration, naming the overloads it shadows. The IR carries no symbol, so
/// each overload is joined back to its Roslyn symbol by its parameter names
/// and types.
/// </summary>
internal sealed class CatchAllOverloadReporter(Action<MetanoDiagnostic> reportDiagnostic)
{
    private readonly Action<MetanoDiagnostic> _reportDiagnostic = reportDiagnostic;

    public void ReportConstructors(
        INamedTypeSymbol type,
        IrConstructorDeclaration primary,
        TypeCheckScope classScope
    )
    {
        var symbols = type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared).ToList();

        var found = CatchAllOverloads.Find(
            [primary, .. primary.Overloads ?? []],
            ctor => ctor.PlainParameters,
            _ => classScope
        );

        foreach (var catchAll in found)
        {
            Report(
                OverloadSymbols.Of(symbols, catchAll.Overload.PlainParameters),
                catchAll.Shadowed.Select(ctor => OverloadSymbols.Of(symbols, ctor.PlainParameters)),
                type
            );
        }
    }

    public void ReportMethods(
        INamedTypeSymbol type,
        IrMethodDeclaration primary,
        TypeCheckScope classScope
    )
    {
        var symbols = type.GetMembers(primary.Name).OfType<IMethodSymbol>().ToList();

        var found = CatchAllOverloads.Find(
            IrMethodGroup.Of(primary).Branches,
            method => method.Parameters,
            method =>
                TypeCheckScope.ForMethod(
                    method,
                    classScope.TypeParameters,
                    classScope.TypeOverrides
                )
        );

        foreach (var catchAll in found)
        {
            Report(
                OverloadSymbols.Of(symbols, catchAll.Overload.Parameters),
                catchAll.Shadowed.Select(method => OverloadSymbols.Of(symbols, method.Parameters)),
                type
            );
        }
    }

    private void Report(
        IMethodSymbol? catchAll,
        IEnumerable<IMethodSymbol?> shadowed,
        INamedTypeSymbol type
    )
    {
        var shadowedNames = string.Join(
            ", ",
            shadowed.Select(symbol => $"'{OverloadSymbols.Describe(symbol)}'")
        );

        _reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousRuntimeDispatch,
                $"Overload '{OverloadSymbols.Describe(catchAll)}' accepts any argument at runtime, so the "
                    + $"TypeScript dispatcher tries it before {shadowedNames}, whose argument "
                    + "counts it overlaps: a call C# binds to one of them can reach it "
                    + "instead (ambiguous runtime dispatch). Static call-site dispatch (#25) "
                    + "will resolve it; until then, give the overloads distinct arities or "
                    + "constrain the type parameter.",
                catchAll?.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault()
            )
        );
    }
}
