using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Reports <c>MS0062</c> when a <c>SerializerContext</c> constructs a class of a
/// referenced assembly through its <c>TypeSpec</c> (<c>type: T</c> /
/// <c>new T(...)</c>) and that class's constructor is not public. The
/// referenced package decided the TypeScript modifier when it was transpiled
/// (FR-049), so the consumer cannot widen it and the context does not
/// type-check. The modifier is judged by
/// <see cref="TsConstructorModifier.DeclaredModifierOf"/>, the predicate the
/// emitter uses. Known false positive: a type the package itself kept public
/// because code outside its class body constructs it (<c>MS0061</c>), which
/// metadata does not show.
/// </summary>
internal sealed class JsonReferencedConstructorValidator(
    IAssemblySymbol currentAssembly,
    Action<MetanoDiagnostic> reportDiagnostic
)
{
    private readonly IAssemblySymbol _currentAssembly = currentAssembly;
    private readonly Action<MetanoDiagnostic> _reportDiagnostic = reportDiagnostic;

    public void Validate(
        IReadOnlyDictionary<
            INamedTypeSymbol,
            IReadOnlyList<INamedTypeSymbol>
        > constructedTypesByContext
    )
    {
        foreach (var (context, constructedTypes) in constructedTypesByContext)
        {
            foreach (var type in constructedTypes.Where(IsReferencedWithoutPublicConstructor))
                _reportDiagnostic(NotConstructible(context, type));
        }
    }

    private bool IsReferencedWithoutPublicConstructor(INamedTypeSymbol type) =>
        IsFromReferencedAssembly(type)
        && TsConstructorModifier.DeclaredModifierOf(type).IsNonPublic();

    private bool IsFromReferencedAssembly(INamedTypeSymbol type) =>
        !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _currentAssembly);

    private static MetanoDiagnostic NotConstructible(
        INamedTypeSymbol context,
        INamedTypeSymbol type
    ) =>
        new(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.JsonContractConstructorNotPublic,
            $"The JSON SerializerContext '{context.Name}' constructs '{type.Name}' of the "
                + $"referenced assembly '{type.ContainingAssembly.Name}', whose constructor "
                + "is not public there; the generated TypeSpec does not type-check. Give "
                + $"'{type.Name}' a public constructor or make it [PlainObject].",
            context.Locations.FirstOrDefault()
        );
}
