using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Reports <c>MS0063</c> for each group of overloads a dispatcher cannot
/// tell apart because of a branded 64-bit type (see
/// <see cref="BrandedSixtyFourBitOverloads"/>), once per group, at the first
/// branded overload the type declares in the group (else its first declared
/// one), naming all of them. A dispatcher that overrides an inherited one is
/// judged over the inherited overloads plus the ones the type adds, since its
/// own arms run before the base's. Function dispatchers (module functions,
/// extension methods, a branded companion's static groups) are reported the
/// same way through <see cref="ReportFunctions"/>.
/// <para>
/// The message says what a call transpiled from C# does: it names the
/// overload's own fast path, so the ambiguity cannot reach it. The operators
/// and conversions of a <c>[Branded]</c> / <c>[InlineWrapper]</c> type are
/// the exception — their applications are not lowered to the declared
/// functions at all (#599) — and the message says that instead.
/// </para>
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
            Report(
                group.Select(ctor => (OverloadSymbols.Of(symbols, ctor.PlainParameters), true)),
                type
            );
        }
    }

    public void ReportMethods(INamedTypeSymbol type, IrMethodDeclaration primary)
    {
        var methodGroup = IrMethodGroup.Of(primary);
        var added = methodGroup.Branches;
        var symbols = OverloadSymbols.NamedInHierarchy(type, primary.Name);

        var groups = BrandedSixtyFourBitOverloads.Find(
            [.. primary.InheritedOverloads ?? [], .. added],
            method => method.Parameters
        );

        var isBrandedOperator = IsBrandedOperator(type, primary.Semantics.IsOperator);

        foreach (var group in groups.Where(group => group.Any(added.Contains)))
        {
            Report(
                group.Select(method =>
                    (OverloadSymbols.Of(symbols, method.Parameters), added.Contains(method))
                ),
                type,
                isBrandedOperator
            );
        }
    }

    /// <summary>
    /// The overloads of a function dispatcher named <paramref name="name"/>
    /// on <paramref name="type"/>: a module-function or extension group, or
    /// a static group of a branded companion namespace. Such a dispatcher
    /// inherits nothing, so every overload is one the type declares.
    /// </summary>
    public void ReportFunctions(
        INamedTypeSymbol type,
        string name,
        IReadOnlyList<FunctionOverloadShape> overloads
    )
    {
        var symbols = type.GetMembers(name).OfType<IMethodSymbol>().ToList();
        var groups = BrandedSixtyFourBitOverloads.Find(overloads, overload => overload.Parameters);
        var isBrandedOperator = IsBrandedOperator(type, overloads[0].IsOperator);

        foreach (var group in groups)
        {
            Report(
                group.Select(overload => (OverloadSymbols.Of(symbols, overload.Parameters), true)),
                type,
                isBrandedOperator
            );
        }
    }

    private void Report(
        IEnumerable<(IMethodSymbol? Symbol, bool IsAdded)> members,
        INamedTypeSymbol type,
        bool isBrandedOperator = false
    )
    {
        var ordered = members
            .OrderBy(member => member.Symbol is null)
            .ThenBy(member => PathOf(member.Symbol), StringComparer.Ordinal)
            .ThenBy(member => StartOf(member.Symbol))
            .ToList();

        var anchor = ordered
            .Where(member => member.IsAdded)
            .OrderByDescending(member => HasBrandedParameter(member.Symbol))
            .Select(member => member.Symbol?.Locations.FirstOrDefault())
            .FirstOrDefault(location => location is not null);

        _reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousRuntimeDispatch,
                $"Overloads {OverloadSymbols.DescribeAll(ordered.Select(member => member.Symbol))} "
                    + "share one runtime test: a "
                    + "branded 64-bit type ([Branded] or [InlineWrapper] over long, ulong, nint, "
                    + "nuint or BigInteger) lowers to a bigint, so its dispatcher arm accepts "
                    + "every value the long, ulong and BigInteger arms accept. A call that "
                    + "reaches the dispatcher (hand-written TypeScript, or a consumer reading "
                    + "this type from a DLL or package) can run another overload than the one C# "
                    + $"binds; {TranspiledCallsNote(isBrandedOperator)} To make the dispatcher unambiguous, "
                    + "give the overloads distinct arities or a distinct non-64-bit parameter.",
                anchor ?? type.Locations.FirstOrDefault()
            )
        );
    }

    /// <summary>
    /// Whether the group holds the operators or conversions of a
    /// <c>[Branded]</c> / <c>[InlineWrapper]</c> type, whose applications no
    /// generated code lowers to the declared functions (#599).
    /// </summary>
    private static bool IsBrandedOperator(INamedTypeSymbol type, bool isOperatorGroup) =>
        isOperatorGroup && SymbolHelper.HasBranded(type);

    private static string TranspiledCallsNote(bool isBrandedOperator) =>
        isBrandedOperator
            ? "an operator or conversion of a branded type applied in C# is not lowered to "
                + "these functions (#599)."
            : "calls transpiled from C# in this compilation use the overload's own fast path "
                + "and are not affected.";

    private static bool HasBrandedParameter(IMethodSymbol? symbol) =>
        symbol?.Parameters.Any(parameter => IsBranded(parameter.Type)) ?? false;

    private static bool IsBranded(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => IsBranded(array.ElementType),
            INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable => IsBranded(nullable.TypeArguments[0]),
            _ => type.HasBranded(),
        };

    private static string PathOf(IMethodSymbol? symbol) =>
        symbol?.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? string.Empty;

    private static int StartOf(IMethodSymbol? symbol) =>
        symbol?.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;
}
