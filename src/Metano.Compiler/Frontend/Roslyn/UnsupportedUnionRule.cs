using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// <c>MS0038</c> (spec 010 FR-012a, decision W7-2): a C# 15 union type
/// (<see cref="RoslynTypeQueries.IsUnionType"/>) is not supported until the union
/// slices land. A union reaches the output two ways, and both report here:
/// <list type="bullet">
///   <item>its own declaration is transpiled (<see cref="ReportDeclaration"/>, called by
///   <see cref="UnionDeclarationValidator"/>);</item>
///   <item>transpiled code uses it, wherever it is declared — the same assembly
///   without <c>[Transpile]</c>, a referenced <c>[EmitPackage]</c> library or a plain
///   referenced assembly (<see cref="ITranspiledReferenceRule"/>, fed by
///   <see cref="TranspiledReferenceWalker"/>). A referenced member counts through its
///   signature, so <c>lib.Make().Value</c> reports although no union is spelled.</item>
/// </list>
/// Each distinct union is reported once per file, at its first site there; a
/// generic union is one union whatever its type arguments.
/// </summary>
internal sealed class UnsupportedUnionRule(List<MetanoDiagnostic> diagnostics)
    : ITranspiledReferenceRule
{
    private readonly List<MetanoDiagnostic> _diagnostics = diagnostics;
    private readonly HashSet<(SyntaxTree File, ISymbol Union)> _reported = new(
        new ReportKeyComparer()
    );

    public void ReportDeclaration(INamedTypeSymbol union)
    {
        var location = union.Locations.FirstOrDefault();

        if (TryClaim(union, location))
            _diagnostics.Add(Diagnostic($"Union declaration '{union.Name}'", location));
    }

    public void CheckType(INamedTypeSymbol type, Location location) =>
        ReportUse(FindUnion(type), location);

    public void CheckMember(ISymbol member, Location location)
    {
        foreach (var type in SignatureTypes(member))
            ReportUse(FindUnion(type), location);
    }

    private void ReportUse(INamedTypeSymbol? union, Location location)
    {
        if (union is not null && TryClaim(union, location))
            _diagnostics.Add(Diagnostic($"Union type '{union.ToDisplayString()}'", location));
    }

    private bool TryClaim(INamedTypeSymbol union, Location? location) =>
        location?.SourceTree is not { } file || _reported.Add((file, union.OriginalDefinition));

    /// <summary>
    /// The types a referenced member brings into the expression that uses it: its
    /// value type and its parameter types, so a call that converts an argument to a
    /// union parameter reports as well.
    /// </summary>
    private static IEnumerable<ITypeSymbol> SignatureTypes(ISymbol member) =>
        member switch
        {
            IMethodSymbol method => method
                .Parameters.Select(p => p.Type)
                .Prepend(method.ReturnType),
            IPropertySymbol property => property
                .Parameters.Select(p => p.Type)
                .Prepend(property.Type),
            IFieldSymbol field => [field.Type],
            IEventSymbol @event => [@event.Type],
            _ => [],
        };

    /// <summary>
    /// The first union in <paramref name="type"/>, its array elements or its type
    /// arguments (<c>List&lt;Pet&gt;</c>, <c>Pet[]</c>).
    /// </summary>
    private static INamedTypeSymbol? FindUnion(ITypeSymbol type) =>
        type switch
        {
            INamedTypeSymbol named when RoslynTypeQueries.IsUnionType(named) => named,
            INamedTypeSymbol named => named
                .TypeArguments.Select(FindUnion)
                .FirstOrDefault(u => u is not null),
            IArrayTypeSymbol array => FindUnion(array.ElementType),
            _ => null,
        };

    private static MetanoDiagnostic Diagnostic(string subject, Location? location) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.UnsupportedUnionShape,
            $"{subject} is not supported by this Metano version; C# 15 unions are planned "
                + "(spec 010). Use a closed class hierarchy instead, or keep the union out "
                + "of the transpiled types.",
            location
        );

    private sealed class ReportKeyComparer : IEqualityComparer<(SyntaxTree File, ISymbol Union)>
    {
        public bool Equals(
            (SyntaxTree File, ISymbol Union) x,
            (SyntaxTree File, ISymbol Union) y
        ) => x.File == y.File && SymbolEqualityComparer.Default.Equals(x.Union, y.Union);

        public int GetHashCode((SyntaxTree File, ISymbol Union) key) =>
            HashCode.Combine(key.File, SymbolEqualityComparer.Default.GetHashCode(key.Union));
    }
}
