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
    private static readonly SymbolDisplayFormat SignatureFormat =
        SymbolDisplayFormat.CSharpShortErrorMessageFormat;

    private readonly Action<MetanoDiagnostic> _reportDiagnostic = reportDiagnostic;

    public void ReportConstructors(
        INamedTypeSymbol type,
        IrConstructorDeclaration primary,
        IReadOnlyList<IrTypeParameter> classTypeParameters
    )
    {
        var symbols = type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared).ToList();

        var found = CatchAllOverloads.Find(
            [primary, .. primary.Overloads ?? []],
            ctor => ctor.Parameters.Select(p => p.Parameter).ToList(),
            _ => classTypeParameters
        );

        foreach (var catchAll in found)
        {
            Report(
                SymbolOf(symbols, catchAll.Overload.Parameters.Select(p => p.Parameter).ToList()),
                catchAll.Shadowed.Select(ctor =>
                    SymbolOf(symbols, ctor.Parameters.Select(p => p.Parameter).ToList())
                ),
                type
            );
        }
    }

    public void ReportMethods(
        INamedTypeSymbol type,
        IrMethodDeclaration primary,
        IReadOnlyList<IrTypeParameter> classTypeParameters
    )
    {
        var symbols = type.GetMembers(primary.Name).OfType<IMethodSymbol>().ToList();

        var found = CatchAllOverloads.Find(
            [primary, .. primary.Overloads ?? []],
            method => method.Parameters,
            method => [.. method.TypeParameters ?? [], .. classTypeParameters]
        );

        foreach (var catchAll in found)
        {
            Report(
                SymbolOf(symbols, catchAll.Overload.Parameters),
                catchAll.Shadowed.Select(method => SymbolOf(symbols, method.Parameters)),
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
        var shadowedNames = string.Join(", ", shadowed.Select(symbol => $"'{Describe(symbol)}'"));

        _reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousRuntimeDispatch,
                $"Overload '{Describe(catchAll)}' accepts any argument at runtime, so the "
                    + $"TypeScript dispatcher tries it before {shadowedNames}, whose argument "
                    + "counts it overlaps: a call C# binds to one of them can reach it "
                    + "instead (ambiguous runtime dispatch). Static call-site dispatch (#25) "
                    + "will resolve it; until then, give the overloads distinct arities or "
                    + "constrain the type parameter.",
                catchAll?.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault()
            )
        );
    }

    private static string Describe(IMethodSymbol? symbol) =>
        symbol?.ToDisplayString(SignatureFormat) ?? "an overload";

    /// <summary>
    /// The declared overload whose parameters match <paramref name="parameters"/>
    /// by name and <c>params</c> flag, then, among several, by mapped type.
    /// </summary>
    private static IMethodSymbol? SymbolOf(
        IReadOnlyList<IMethodSymbol> symbols,
        IReadOnlyList<IrParameter> parameters
    )
    {
        var byName = symbols
            .Where(symbol => HasParameters(symbol, parameters, MatchesByName))
            .ToList();

        return byName.Count == 1
            ? byName[0]
            : byName.FirstOrDefault(symbol => HasParameters(symbol, parameters, MatchesByType));
    }

    private static bool HasParameters(
        IMethodSymbol symbol,
        IReadOnlyList<IrParameter> parameters,
        Func<IParameterSymbol, IrParameter, bool> matches
    ) =>
        symbol.Parameters.Length == parameters.Count
        && symbol.Parameters.Zip(parameters).All(pair => matches(pair.First, pair.Second));

    private static bool MatchesByName(IParameterSymbol symbol, IrParameter parameter) =>
        symbol.Name == parameter.Name && symbol.IsParams == parameter.IsParams;

    private static bool MatchesByType(IParameterSymbol symbol, IrParameter parameter) =>
        IrTypeRefSignatures.Describe(IrTypeRefMapper.Map(symbol.Type))
        == IrTypeRefSignatures.Describe(parameter.Type);
}
