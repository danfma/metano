using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Decides the TypeScript modifier of a class constructor (FR-049) from the
/// type's IR and the compilation-wide <see cref="ConstructorAccessFacts"/>;
/// the Roslyn symbol only keys the facts.
/// <list type="number">
///   <item>Each declared visibility maps through
///     <see cref="TsConstructorModifier"/>; a value type, whose implicit
///     parameterless constructor is always public, keeps a public one;</item>
///   <item>a TypeScript class has one constructor accessibility (TS2385), so
///     mixed constructors emit the most permissive one and report
///     <c>MS0060</c>;</item>
///   <item>a type that code outside its class body constructs keeps a public
///     constructor and reports <c>MS0061</c>;</item>
///   <item>a <c>private</c> constructor of a type an emitted class extends
///     becomes <c>protected</c> (TS2675).</item>
/// </list>
/// </summary>
public sealed class ConstructorAccessPolicy(
    ConstructorAccessFacts constructorAccessFacts,
    Action<MetanoDiagnostic> reportDiagnostic
)
{
    private readonly ConstructorAccessFacts _constructorAccessFacts = constructorAccessFacts;
    private readonly Action<MetanoDiagnostic> _reportDiagnostic = reportDiagnostic;

    /// <summary>
    /// The modifier of the constructor emitted for <paramref name="type"/>
    /// (whose IR is <paramref name="ir"/>), reporting <c>MS0060</c> /
    /// <c>MS0061</c> on the way. The class emitter calls it once per type.
    /// </summary>
    public TsAccessibility ResolveAndReport(INamedTypeSymbol type, IrClassDeclaration ir)
    {
        if (
            ir.Constructor is not { } constructor
            || TsConstructorModifier.KeepsPublicConstructor(ir)
        )
            return TsAccessibility.Public;

        var mostPermissive = TsConstructorModifier.MostPermissiveOf(constructor);
        ReportNarrowerConstructors(type, mostPermissive);

        if (mostPermissive is TsAccessibility.Public)
            return mostPermissive;

        if (_constructorAccessFacts.OutsideConstructionOf(type) is { } construction)
        {
            ReportWidened(type, mostPermissive, construction);

            return TsAccessibility.Public;
        }

        return mostPermissive is TsAccessibility.Private && _constructorAccessFacts.IsExtended(type)
            ? TsAccessibility.Protected
            : mostPermissive;
    }

    private void ReportNarrowerConstructors(INamedTypeSymbol type, TsAccessibility emitted)
    {
        var narrower = _constructorAccessFacts
            .ConstructorsOf(type)
            .Where(constructor => constructor.Modifier.IsNarrowerThan(emitted))
            .ToList();

        if (narrower.Count == 0)
            return;

        var names = string.Join(", ", narrower.Select(constructor => constructor.Description));

        _reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.MixedConstructorAccessibility,
                $"The constructors of '{type.Name}' mix accessibility levels; a TypeScript "
                    + $"class has one constructor accessibility, so it is emitted {emitted.ToKeyword()}. "
                    + $"TypeScript does not enforce the narrower constructors: {names}.",
                narrower[0].Location
            )
        );
    }

    private void ReportWidened(
        INamedTypeSymbol type,
        TsAccessibility declared,
        OutsideConstruction construction
    ) =>
        _reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.ConstructorAccessibilityWidened,
                $"The constructor of '{type.Name}' is emitted public instead of "
                    + $"{declared.ToKeyword()} because {construction.Site} constructs it outside "
                    + "the TypeScript class body.",
                _constructorAccessFacts
                    .ConstructorsOf(type)
                    .FirstOrDefault(constructor => constructor.Modifier == declared)
                    ?.Location
            )
        );
}
