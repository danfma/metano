using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>[JsonFactory]</c> a <c>[Branded]</c> type's descriptors call instead of
/// <c>create</c>, and the TypeScript member name it is declared under in the
/// companion namespace.
/// </summary>
/// <param name="Method">The validated factory.</param>
/// <param name="TsName">The companion member name, <c>from</c> for <c>From</c>.</param>
internal sealed record JsonBrandedFactory(IMethodSymbol Method, string TsName);

/// <summary>
/// Finds and validates the <c>[JsonFactory]</c> methods of one type (spec 009 FR-021
/// to FR-023). <see cref="Resolve"/> and <see cref="DiagnosticsFor"/> both go through
/// the same evaluation, so what is reported and what is emitted cannot disagree. A
/// type whose factory is invalid keeps the non-validating <c>create</c>.
/// </summary>
internal static class JsonBrandedFactoryResolver
{
    /// <summary>
    /// Members the branded companion synthesizes; a factory emitted under one of
    /// these names would collide with it.
    /// </summary>
    private static readonly string[] SynthesizedCompanionMembers =
    [
        IrToTsBrandedBridge.CreateMember,
        IrToTsBrandedBridge.ToStringMember,
    ];

    public static JsonBrandedFactory? Resolve(INamedTypeSymbol type) => Evaluate(type).Factory;

    public static IReadOnlyList<MetanoDiagnostic> DiagnosticsFor(INamedTypeSymbol type) =>
        Evaluate(type).Diagnostics;

    private static Evaluation Evaluate(INamedTypeSymbol type)
    {
        var candidates = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(IsFactoryCandidate)
            .ToList();

        if (candidates.Count == 0)
        {
            return Evaluation.None;
        }

        var underlyingType = SymbolHelper.GetBrandedValueType(type);

        if (underlyingType is null)
        {
            return new Evaluation(null, NotBrandedErrors(type, candidates));
        }

        var diagnostics = candidates
            .Select(method => InvalidFactoryError(type, method, underlyingType))
            .OfType<MetanoDiagnostic>()
            .Concat(DuplicateFactoryErrors(type, candidates))
            .ToList();

        var factory =
            diagnostics.Count == 0
                ? new JsonBrandedFactory(candidates[0], TsNameOf(candidates[0]))
                : null;

        return new Evaluation(factory, diagnostics);
    }

    private static bool IsFactoryCandidate(IMethodSymbol method) =>
        !method.IsImplicitlyDeclared && method.HasJsonFactory();

    private static List<MetanoDiagnostic> NotBrandedErrors(
        INamedTypeSymbol type,
        IEnumerable<IMethodSymbol> candidates
    ) =>
        candidates
            .Select(method =>
                Error(
                    method,
                    $"[JsonFactory] is only valid on a [Branded] type; '{type.Name}' is not branded."
                )
            )
            .ToList();

    private static IEnumerable<MetanoDiagnostic> DuplicateFactoryErrors(
        INamedTypeSymbol type,
        IEnumerable<IMethodSymbol> candidates
    ) =>
        candidates
            .Skip(1)
            .Select(method =>
                Error(method, $"'{type.Name}' declares more than one [JsonFactory] method.")
            );

    private static MetanoDiagnostic? InvalidFactoryError(
        INamedTypeSymbol type,
        IMethodSymbol method,
        ITypeSymbol underlyingType
    )
    {
        var qualifiedName = $"{type.Name}.{method.Name}";
        var tsName = TsNameOf(method);
        var sibling = SiblingSharingTsName(type, method, tsName);

        return method switch
        {
            { MethodKind: not MethodKind.Ordinary } => Error(
                method,
                $"[JsonFactory] cannot be applied to operator '{qualifiedName}'; use a named static method."
            ),
            { IsStatic: false } => Error(
                method,
                $"[JsonFactory] method '{qualifiedName}' must be static."
            ),
            { IsGenericMethod: true } => Error(
                method,
                $"[JsonFactory] '{qualifiedName}' must not be generic."
            ),
            _ when IsNotEmitted(method) => Error(
                method,
                $"[JsonFactory] '{qualifiedName}' is not emitted to TypeScript."
            ),
            _ when IsHiddenFromPackageConsumers(method) => Error(
                method,
                $"[JsonFactory] '{qualifiedName}' must be public in a package library; consumers compiled against its metadata cannot see it."
            ),
            _ when !TakesSingleParameterOf(method, underlyingType) => Error(
                method,
                $"[JsonFactory] '{qualifiedName}' must take exactly one '{underlyingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' parameter."
            ),
            _ when !SameType(method.ReturnType, type) => Error(
                method,
                $"[JsonFactory] '{qualifiedName}' must return '{type.Name}'."
            ),
            _ when sibling is not null && sibling.Name == method.Name => Error(
                method,
                $"[JsonFactory] '{qualifiedName}' is overloaded; the TypeScript dispatcher could call a different overload."
            ),
            _ when sibling is not null => Error(
                method,
                $"[JsonFactory] '{qualifiedName}' emits as '{tsName}', which '{type.Name}.{sibling.Name}' also uses; rename one or add [Name]."
            ),
            _ when SynthesizedCompanionMembers.Contains(tsName) => Error(
                method,
                $"[JsonFactory] '{qualifiedName}' emits as '{tsName}', which the branded companion already declares; rename it or add [Name]."
            ),
            _ => null,
        };
    }

    private static bool IsNotEmitted(IMethodSymbol method) =>
        method.HasIgnore(TargetLanguage.TypeScript) || method.IsInlinedAtCallSite();

    /// <summary>
    /// A non-public member of a package library is absent from the reference
    /// assembly a consumer compiles against, so the consumer's context would silently
    /// fall back to <c>create</c>. Rejecting it where it is declared keeps the
    /// descriptor the same for every consumer.
    /// </summary>
    private static bool IsHiddenFromPackageConsumers(IMethodSymbol method) =>
        method.DeclaredAccessibility != Accessibility.Public
        && method.ContainingAssembly is { } assembly
        && SymbolHelper.GetEmitPackageInfo(assembly, (int)TargetLanguage.TypeScript) is not null;

    /// <summary>
    /// Another method of the type the companion declares under the factory's
    /// TypeScript name: an overload, or a method renamed onto it with <c>[Name]</c>.
    /// </summary>
    private static IMethodSymbol? SiblingSharingTsName(
        INamedTypeSymbol type,
        IMethodSymbol method,
        string tsName
    ) =>
        type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(other => other.MethodKind == MethodKind.Ordinary)
            .FirstOrDefault(other =>
                !SymbolEqualityComparer.Default.Equals(other, method) && TsNameOf(other) == tsName
            );

    private static bool TakesSingleParameterOf(IMethodSymbol method, ITypeSymbol underlyingType) =>
        method.Parameters is [var parameter] && SameType(parameter.Type, underlyingType);

    private static bool SameType(ITypeSymbol left, ITypeSymbol right) =>
        SymbolEqualityComparer.Default.Equals(left, right);

    /// <summary>
    /// The name the branded companion declares the method with
    /// (<see cref="IrToTsNamingPolicy.ToFunctionName"/>, honoring <c>[Name]</c>), the
    /// same function <c>IrToTsBrandedBridge</c> uses, so the descriptor names exactly
    /// what the companion declares.
    /// </summary>
    private static string TsNameOf(IMethodSymbol method) =>
        IrToTsNamingPolicy.ToFunctionName(method.Name, IrAttributeExtractor.Extract(method));

    private static MetanoDiagnostic Error(IMethodSymbol method, string message) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.InvalidJsonFactory,
            message,
            method.Locations.FirstOrDefault()
        );

    private sealed record Evaluation(
        JsonBrandedFactory? Factory,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    )
    {
        public static readonly Evaluation None = new(null, []);
    }
}
