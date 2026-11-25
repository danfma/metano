using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Reports <c>MS0063</c> at each overload a class or module-function dispatcher drops
/// because an earlier overload has the same runtime test (#778, see
/// <see cref="ShadowedDispatchOverloads"/>), naming the fast path a
/// hand-written TypeScript caller must call to reach it. A pair that
/// differs only by a branded 64-bit type is already reported, once per group,
/// by <see cref="BrandedSixtyFourBitReporter"/>.
/// </summary>
internal static class ShadowedOverloadReporter
{
    public static void ReportMethods(
        INamedTypeSymbol type,
        IrMethodDeclaration primary,
        IReadOnlyList<IrTypeParameter> classTypeParameters,
        TsOverloadClaims claims,
        IrToTsBridgeContext context,
        Action<MetanoDiagnostic> report
    )
    {
        var brandedOrdinals = BrandedSixtyFourBitOrdinals(primary);

        var found = IrToTsOverloadDispatcherBridge
            .ShadowedOverloadsOf(primary, classTypeParameters, context)
            .Where(shadowed =>
                !brandedOrdinals.Contains(shadowed.Overload.DeclarationOrdinal)
                || !brandedOrdinals.Contains(shadowed.TakenBy.DeclarationOrdinal)
            )
            .ToList();

        if (found.Count == 0)
            return;

        var symbols = type.GetMembers(primary.Name).OfType<IMethodSymbol>().ToList();

        foreach (var shadowed in found)
            report(DiagnosticOf(type, symbols, shadowed, claims));
    }

    /// <summary>
    /// The constructor overloads the constructor dispatcher drops; each is
    /// reached through its factory (spec 013 FR-009).
    /// </summary>
    public static void ReportConstructors(
        INamedTypeSymbol type,
        IrConstructorDeclaration primary,
        IReadOnlyList<IrTypeParameter> typeParameters,
        string? typeArgumentSlot,
        TsOverloadClaims claims,
        IrToTsBridgeContext context,
        Action<MetanoDiagnostic> report
    )
    {
        var branded = BrandedSixtyFourBitOverloads
            .Find([primary, .. primary.Overloads ?? []], ctor => ctor.PlainParameters)
            .SelectMany(group => group)
            .Select(ctor => ctor.DeclarationOrdinal)
            .ToHashSet();

        var found = IrToTsConstructorDispatcherBridge
            .ShadowedConstructorsOf(primary, typeParameters, typeArgumentSlot, context)
            .Where(shadowed =>
                !branded.Contains(shadowed.Overload.DeclarationOrdinal)
                || !branded.Contains(shadowed.TakenBy.DeclarationOrdinal)
            )
            .ToList();

        if (found.Count == 0)
            return;

        var symbols = type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared).ToList();

        foreach (var shadowed in found)
        {
            var factory = claims.ConstructorNamesOf(shadowed.Overload.DeclarationOrdinal)?.Factory;

            report(
                DiagnosticOf(
                    type,
                    OverloadSymbols.Of(symbols, shadowed.Overload.PlainParameters),
                    OverloadSymbols.Of(symbols, shadowed.TakenBy.PlainParameters),
                    "constructor",
                    factory is null ? type.Name : $"{type.Name}.{factory}"
                )
            );
        }
    }

    /// <summary>
    /// The overloads a module-function group's dispatcher (an
    /// <c>[ExportedAsModule]</c> / <c>[NoContainer]</c> class or an
    /// extension container) drops; each is reached through its exported fast
    /// path.
    /// </summary>
    public static void ReportFunctions(
        INamedTypeSymbol type,
        IReadOnlyList<IrModuleFunction> overloads,
        TsModuleFunctionClaims claims,
        IrToTsBridgeContext context,
        Action<MetanoDiagnostic> report
    )
    {
        var brandedSignatures = BrandedSixtyFourBitOverloads
            .Find(overloads, function => function.Parameters)
            .SelectMany(group => group)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        var found = IrToTsModuleBridge
            .ShadowedOverloadsOf(overloads, context)
            .Where(shadowed =>
                !brandedSignatures.Contains(shadowed.Overload)
                || !brandedSignatures.Contains(shadowed.TakenBy)
            )
            .ToList();

        if (found.Count == 0)
            return;

        var symbols = type.GetMembers(overloads[0].Name).OfType<IMethodSymbol>().ToList();

        foreach (var shadowed in found)
        {
            report(
                DiagnosticOf(
                    type,
                    SymbolOf(
                        symbols,
                        shadowed.Overload.Parameters,
                        shadowed.Overload.SignatureText
                    ),
                    SymbolOf(symbols, shadowed.TakenBy.Parameters, shadowed.TakenBy.SignatureText),
                    claims.ClaimOf(shadowed.Overload.OverloadRef!)?.Dispatcher ?? overloads[0].Name,
                    claims.ClaimOf(shadowed.Overload.OverloadRef!)?.FastPath ?? overloads[0].Name
                )
            );
        }
    }

    private static HashSet<int> BrandedSixtyFourBitOrdinals(IrMethodDeclaration primary) =>
        BrandedSixtyFourBitOverloads
            .Find(IrMethodGroup.Of(primary).Branches, method => method.Parameters)
            .SelectMany(group => group)
            .Select(method => method.DeclarationOrdinal)
            .ToHashSet();

    private static MetanoDiagnostic DiagnosticOf(
        INamedTypeSymbol type,
        IReadOnlyList<IMethodSymbol> symbols,
        ShadowedOverload<IrMethodDeclaration> shadowed,
        TsOverloadClaims claims
    ) =>
        DiagnosticOf(
            type,
            SymbolOf(symbols, shadowed.Overload.Parameters, shadowed.Overload.SignatureText),
            SymbolOf(symbols, shadowed.TakenBy.Parameters, shadowed.TakenBy.SignatureText),
            TsClassMemberTable.MethodNameOf(shadowed.Overload),
            CalleeOf(type, shadowed.Overload, claims)
        );

    private static MetanoDiagnostic DiagnosticOf(
        INamedTypeSymbol type,
        IMethodSymbol? overload,
        IMethodSymbol? takenBy,
        string dispatcher,
        string callee
    ) =>
        new(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.AmbiguousRuntimeDispatch,
            $"Overload '{OverloadSymbols.Describe(overload)}' has the same runtime test as "
                + $"'{OverloadSymbols.Describe(takenBy)}' in the TypeScript dispatcher "
                + $"'{dispatcher}', which therefore leaves it out. C# call sites bind to it "
                + $"directly; hand-written TypeScript must call '{callee}'.",
            overload?.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault()
        );

    private static IMethodSymbol? SymbolOf(
        IReadOnlyList<IMethodSymbol> symbols,
        IReadOnlyList<IrParameter> parameters,
        string? signatureText
    ) => OverloadSymbols.Of(symbols, parameters, signatureText);

    private static string CalleeOf(
        INamedTypeSymbol type,
        IrMethodDeclaration overload,
        TsOverloadClaims claims
    )
    {
        var fastPath = claims.FastPathNameOf(overload);

        return overload.IsStatic ? $"{type.Name}.{fastPath}" : fastPath;
    }
}
