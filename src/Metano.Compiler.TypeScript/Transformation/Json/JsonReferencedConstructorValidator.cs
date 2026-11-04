using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Reports <c>MS0062</c> when a <c>SerializerContext</c> constructs a class of a
/// referenced assembly through its <c>TypeSpec</c> (<c>type: T</c> /
/// <c>new T(...)</c>) and none of that class's constructors is public. The
/// referenced package decided the TypeScript modifier when it was transpiled
/// (FR-049), so the consumer cannot widen it and the context does not
/// type-check. Read from metadata: a record's positional constructor (C# has
/// no syntax for its accessibility; Roslyn makes it <c>protected</c> on an
/// abstract record) and its copy constructor do not count.
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
        !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _currentAssembly)
        && !type.IsValueType
        && DeclaredConstructors(type) is { Count: > 0 } constructors
        && constructors.All(IsNonPublic);

    private static List<IMethodSymbol> DeclaredConstructors(INamedTypeSymbol type) =>
        type
            .InstanceConstructors.Where(constructor =>
                !IsCopyConstructor(constructor) && !IsPositionalRecordConstructor(constructor)
            )
            .ToList();

    private static bool IsNonPublic(IMethodSymbol constructor) =>
        TsConstructorModifier.Of(IrVisibilityMapper.Map(constructor.DeclaredAccessibility))
            is not TsAccessibility.Public;

    private static bool IsCopyConstructor(IMethodSymbol constructor) =>
        constructor.ContainingType.IsRecord
        && constructor.Parameters is [var single]
        && SymbolEqualityComparer.Default.Equals(single.Type, constructor.ContainingType);

    /// <summary>
    /// Whether <paramref name="constructor"/> is a record's positional one: its
    /// parameter types match a synthesized <c>Deconstruct</c>.
    /// </summary>
    private static bool IsPositionalRecordConstructor(IMethodSymbol constructor) =>
        constructor.ContainingType.IsRecord
        && constructor
            .ContainingType.GetMembers("Deconstruct")
            .OfType<IMethodSymbol>()
            .Any(deconstruct =>
                deconstruct
                    .Parameters.Select(parameter => parameter.Type)
                    .SequenceEqual(
                        constructor.Parameters.Select(parameter => parameter.Type),
                        SymbolEqualityComparer.Default
                    )
            );

    private static MetanoDiagnostic NotConstructible(
        INamedTypeSymbol context,
        INamedTypeSymbol type
    ) =>
        new(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.JsonContractConstructorNotPublic,
            $"The JSON SerializerContext '{context.Name}' constructs '{type.Name}' of the "
                + $"referenced assembly '{type.ContainingAssembly.Name}', whose constructors "
                + "are not public there; the generated TypeSpec does not type-check. Give "
                + $"'{type.Name}' a public constructor or make it [PlainObject].",
            context.Locations.FirstOrDefault()
        );
}
