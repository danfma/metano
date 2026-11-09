using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Reports <c>MS0063</c> for each group of overloads a BigInt-mode dispatcher
/// cannot tell apart because of a branded 64-bit type (see
/// <see cref="BrandedSixtyFourBitOverloads"/>), once per group, at the first
/// overload the type declares in the group, naming all of them. A dispatcher
/// that overrides an inherited one is judged over the inherited overloads
/// plus the ones the type adds, since its own arms run before the base's.
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
                group.Select(ctor => OverloadSymbols.Of(symbols, ctor.PlainParameters)),
                added: _ => true,
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

        foreach (var group in groups.Where(group => group.Any(added.Contains)))
        {
            var addedSymbols = group
                .Where(added.Contains)
                .Select(method => OverloadSymbols.Of(symbols, method.Parameters))
                .ToList();

            Report(
                group.Select(method => OverloadSymbols.Of(symbols, method.Parameters)),
                addedSymbols.Contains,
                type
            );
        }
    }

    private void Report(
        IEnumerable<IMethodSymbol?> members,
        Func<IMethodSymbol?, bool> added,
        INamedTypeSymbol type
    )
    {
        var ordered = members.OrderBy(PositionOf).ToList();
        var anchor = ordered.FirstOrDefault(added)?.Locations.FirstOrDefault();

        _reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.AmbiguousRuntimeDispatch,
                $"Overloads {OverloadSymbols.DescribeAll(ordered)} share one runtime test: a "
                    + "branded 64-bit type ([Branded] or [InlineWrapper] over long, ulong, nint, "
                    + "nuint or BigInteger) lowers to a bigint, so its dispatcher arm accepts "
                    + "every value the long, ulong and BigInteger arms accept. A call that "
                    + "reaches the dispatcher (hand-written TypeScript, or a consumer reading "
                    + "this type from a DLL or package) can run another overload than the one C# "
                    + "binds; calls transpiled from C# in this compilation use the overload's "
                    + "own fast path and are not affected. To make the dispatcher unambiguous, "
                    + "give the overloads distinct arities or a distinct non-64-bit parameter.",
                anchor ?? type.Locations.FirstOrDefault()
            )
        );
    }

    /// <summary>Declaration order: file path, then position; undeclared members last.</summary>
    private static (string Path, int Start) PositionOf(IMethodSymbol? symbol) =>
        symbol?.Locations.FirstOrDefault() is { SourceTree: { } tree } location
            ? (tree.FilePath, location.SourceSpan.Start)
            : (string.Empty + "￿", int.MaxValue);
}
