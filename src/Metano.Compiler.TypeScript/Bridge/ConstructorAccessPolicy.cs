using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Decides the TypeScript modifier of a class constructor (FR-049):
/// <list type="number">
///   <item>C# <c>private</c> / <c>private protected</c> map to
///     <c>private</c>, <c>protected</c> to <c>protected</c>, every other
///     accessibility to no modifier;</item>
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
    ConstructorAccessFacts facts,
    Action<MetanoDiagnostic> reportDiagnostic
)
{
    private readonly ConstructorAccessFacts _facts = facts;
    private readonly Action<MetanoDiagnostic> _reportDiagnostic = reportDiagnostic;

    /// <summary>
    /// The modifier of <paramref name="type"/>'s emitted constructor, whose
    /// IR is <paramref name="constructor"/> (<c>null</c> for an implicit one,
    /// which stays public).
    /// </summary>
    public TsAccessibility Resolve(INamedTypeSymbol type, IrConstructorDeclaration? constructor)
    {
        if (constructor is null)
            return TsAccessibility.Public;

        var declared = MostPermissive(constructor);
        ReportNarrowerConstructors(type, declared);

        if (declared is TsAccessibility.Public)
            return declared;

        if (_facts.OutsideConstructionOf(type) is { } construction)
        {
            ReportWidened(type, declared, construction);

            return TsAccessibility.Public;
        }

        return declared is TsAccessibility.Private && _facts.IsExtended(type)
            ? TsAccessibility.Protected
            : declared;
    }

    /// <summary>The TypeScript constructor modifier of a C# accessibility.</summary>
    public static TsAccessibility Map(IrVisibility visibility) =>
        visibility switch
        {
            IrVisibility.Private or IrVisibility.PrivateProtected => TsAccessibility.Private,
            IrVisibility.Protected => TsAccessibility.Protected,
            _ => TsAccessibility.Public,
        };

    /// <summary>The TypeScript constructor modifier of a C# constructor.</summary>
    public static TsAccessibility Map(IMethodSymbol constructor) =>
        Map(IrConstructorExtractor.DeclaredVisibility(constructor));

    private static TsAccessibility MostPermissive(IrConstructorDeclaration constructor) =>
        new[] { constructor }
            .Concat(constructor.Overloads ?? [])
            .Select(declaration => Map(declaration.Visibility))
            .MinBy(Restriction);

    /// <summary>How restrictive a modifier is: public 0, protected 1, private 2.</summary>
    private static int Restriction(TsAccessibility accessibility) =>
        accessibility switch
        {
            TsAccessibility.Private => 2,
            TsAccessibility.Protected => 1,
            _ => 0,
        };

    private void ReportNarrowerConstructors(INamedTypeSymbol type, TsAccessibility emitted)
    {
        var narrower = IrConstructorExtractor
            .ModeledConstructors(type)
            .Where(constructor => Restriction(Map(constructor)) > Restriction(emitted))
            .ToList();

        if (narrower.Count == 0)
            return;

        var names = string.Join(", ", narrower.Select(Describe));

        _reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.MixedConstructorAccessibility,
                $"The constructors of '{type.Name}' mix accessibility levels; a TypeScript "
                    + $"class has one constructor accessibility, so it is emitted {Keyword(emitted)}. "
                    + $"TypeScript does not enforce the narrower constructors: {names}.",
                narrower[0].Locations.FirstOrDefault()
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
                    + $"{Keyword(declared)} because {construction.Site} constructs it outside "
                    + "the TypeScript class body.",
                IrConstructorExtractor
                    .ModeledConstructors(type)
                    .FirstOrDefault(constructor => Map(constructor) == declared)
                    ?.Locations.FirstOrDefault()
            )
        );

    private static string Describe(IMethodSymbol constructor)
    {
        var parameters = constructor.Parameters.Select(parameter =>
            parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        );

        return $"'{constructor.ContainingType.Name}({string.Join(", ", parameters)})' "
            + $"({SyntaxFacts.GetText(constructor.DeclaredAccessibility)})";
    }

    private static string Keyword(TsAccessibility accessibility) =>
        accessibility switch
        {
            TsAccessibility.Private => "private",
            TsAccessibility.Protected => "protected",
            _ => "public",
        };
}
