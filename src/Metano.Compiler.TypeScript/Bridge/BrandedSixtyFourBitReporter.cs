using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Reports <c>MS0063</c> for each group of overloads a BigInt-mode dispatcher
/// cannot tell apart because of a <c>[Branded]</c> 64-bit type (see
/// <see cref="BrandedSixtyFourBitOverloads"/>), once per group, at the first
/// declared member, naming the others.
/// </summary>
internal sealed class BrandedSixtyFourBitReporter(Action<MetanoDiagnostic> reportDiagnostic)
{
    private readonly Action<MetanoDiagnostic> _reportDiagnostic = reportDiagnostic;

    public void ReportConstructors(INamedTypeSymbol type, IrConstructorDeclaration primary)
    {
        var symbols = type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared).ToList();

        var groups = BrandedSixtyFourBitOverloads.Find(
            [primary, .. primary.Overloads ?? []],
            ctor => ctor.PlainParameters
        );

        foreach (var group in groups)
        {
            Report(group.Select(ctor => OverloadSymbols.Of(symbols, ctor.PlainParameters)), type);
        }
    }

    public void ReportMethods(INamedTypeSymbol type, IrMethodDeclaration primary)
    {
        var symbols = type.GetMembers(primary.Name).OfType<IMethodSymbol>().ToList();

        var groups = BrandedSixtyFourBitOverloads.Find(
            IrMethodGroup.Of(primary).Branches,
            method => method.Parameters
        );

        foreach (var group in groups)
        {
            Report(group.Select(method => OverloadSymbols.Of(symbols, method.Parameters)), type);
        }
    }

    private void Report(IEnumerable<IMethodSymbol?> members, INamedTypeSymbol type)
    {
        var ordered = members
            .OrderBy(symbol => symbol?.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
            .ToList();

        var names = string.Join(
            ", ",
            ordered.Select(symbol => $"'{OverloadSymbols.Describe(symbol)}'")
        );

        _reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousRuntimeDispatch,
                $"Overloads {names} cannot be told apart at runtime in the BigInt mode: a "
                    + "[Branded] 64-bit type lowers to a bigint, so its dispatcher arm accepts "
                    + "every value the long, ulong and BigInteger arms accept, and a call C# "
                    + "binds to one of them can reach another (ambiguous runtime dispatch). "
                    + "Static call-site dispatch (#25) will resolve it; until then, give the "
                    + "overloads distinct arities or a distinct non-64-bit parameter.",
                ordered.FirstOrDefault()?.Locations.FirstOrDefault()
                    ?? type.Locations.FirstOrDefault()
            )
        );
    }
}
