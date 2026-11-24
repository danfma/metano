using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The overloads of a <c>[PlainObject]</c> method group, which lowers to one
/// top-level function per overload and no dispatcher (spec 013 FR-012): each
/// takes its <c>[Name]</c> as its whole name, the name its call sites spell
/// (#566). Overloads that would share a function name cannot all be emitted:
/// the first declared one is, and the others are reported, since a call to
/// them would reach it.
/// </summary>
internal static class PlainObjectOverloads
{
    /// <summary>
    /// The overloads of <paramref name="primary"/>'s group that are emitted,
    /// in declaration order: the first of each function name.
    /// </summary>
    public static IReadOnlyList<IrMethodDeclaration> EmittedOf(IrMethodDeclaration primary) =>
        InDeclarationOrder(primary).DistinctBy(FunctionNameOf, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Reports MS0001 at every overload of an instance method of
    /// <paramref name="ir"/> that shares its function name with an earlier
    /// one, so it is not emitted.
    /// </summary>
    public static void ReportCollisions(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        Action<MetanoDiagnostic> report
    )
    {
        var primaries = (ir.Members ?? [])
            .OfType<IrMethodDeclaration>()
            .Where(method => !method.IsStatic && method.Overloads is { Count: > 0 });

        foreach (var primary in primaries)
        {
            var emitted = EmittedOf(primary);

            foreach (var dropped in InDeclarationOrder(primary).Except(emitted))
                report(CollisionOf(type, dropped));
        }
    }

    private static MetanoDiagnostic CollisionOf(INamedTypeSymbol type, IrMethodDeclaration dropped)
    {
        var symbols = type.GetMembers(dropped.Name).OfType<IMethodSymbol>().ToList();
        var symbol = OverloadSymbols.Of(symbols, dropped.Parameters);

        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.UnsupportedFeature,
            $"'{OverloadSymbols.Describe(symbol)}' overloads a method of the [PlainObject] "
                + $"type '{type.Name}' under the function name '{FunctionNameOf(dropped)}' "
                + "an earlier overload already takes; a [PlainObject] emits no overload "
                + "dispatcher, so give each overload a distinct [Name].",
            symbol?.Locations.FirstOrDefault() ?? type.Locations.FirstOrDefault()
        );
    }

    private static IEnumerable<IrMethodDeclaration> InDeclarationOrder(
        IrMethodDeclaration primary
    ) => IrMethodGroup.Of(primary).Members.OrderBy(member => member.DeclarationOrdinal);

    /// <summary>The function an overload lowers to, as its call sites name it.</summary>
    public static string FunctionNameOf(IrMethodDeclaration overload) =>
        IrToTsNamingPolicy.ToFunctionName(overload.Name, overload.Attributes);
}
