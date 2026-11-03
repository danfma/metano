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
/// to FR-023). One evaluation serves both consumers: the descriptor builder reads the
/// factory, and <see cref="JsonBrandedFactoryValidator"/> reports the diagnostics, so
/// what is reported and what is emitted cannot disagree. A type whose factory is
/// invalid keeps the non-validating <c>create</c>.
/// </summary>
internal static class JsonBrandedFactoryResolver
{
    /// <summary>
    /// Members the branded companion synthesizes; a factory emitted under one of
    /// these names would collide with it.
    /// </summary>
    private static readonly string[] SynthesizedCompanionMembers = ["create", "toString"];

    public static JsonBrandedFactory? Resolve(INamedTypeSymbol type) => Evaluate(type).Factory;

    public static IReadOnlyList<MetanoDiagnostic> Validate(INamedTypeSymbol type) =>
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

        var underlying = SymbolHelper.GetBrandedValueType(type);
        var diagnostics = new List<MetanoDiagnostic>();

        if (underlying is null)
        {
            diagnostics.AddRange(
                candidates.Select(method =>
                    Error(
                        method,
                        $"[JsonFactory] is only valid on a [Branded] type; '{type.Name}' is not branded."
                    )
                )
            );

            return new Evaluation(null, diagnostics);
        }

        diagnostics.AddRange(
            candidates
                .Select(method => ShapeError(type, method, underlying))
                .OfType<MetanoDiagnostic>()
        );
        diagnostics.AddRange(
            candidates
                .Skip(1)
                .Select(method =>
                    Error(method, $"'{type.Name}' declares more than one [JsonFactory] method.")
                )
        );

        var factory =
            diagnostics.Count == 0
                ? new JsonBrandedFactory(candidates[0], TsNameOf(candidates[0]))
                : null;

        return new Evaluation(factory, diagnostics);
    }

    private static bool IsFactoryCandidate(IMethodSymbol method) =>
        !method.IsImplicitlyDeclared && method.HasJsonFactory();

    private static MetanoDiagnostic? ShapeError(
        INamedTypeSymbol type,
        IMethodSymbol method,
        ITypeSymbol underlying
    )
    {
        var name = $"{type.Name}.{method.Name}";
        var tsName = TsNameOf(method);

        return method switch
        {
            { MethodKind: not MethodKind.Ordinary } => Error(
                method,
                $"[JsonFactory] cannot be applied to operator '{name}'; use a named static method."
            ),
            { IsStatic: false } => Error(method, $"[JsonFactory] method '{name}' must be static."),
            { IsGenericMethod: true } => Error(
                method,
                $"[JsonFactory] '{name}' must not be generic."
            ),
            _ when IsNotEmitted(method) => Error(
                method,
                $"[JsonFactory] '{name}' is not emitted to TypeScript."
            ),
            _ when !TakesOnly(method, underlying) => Error(
                method,
                $"[JsonFactory] '{name}' must take exactly one '{underlying.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' parameter."
            ),
            _ when !SameType(method.ReturnType, type) => Error(
                method,
                $"[JsonFactory] '{name}' must return '{type.Name}'."
            ),
            _ when IsOverloaded(type, method) => Error(
                method,
                $"[JsonFactory] '{name}' is overloaded; the TypeScript dispatcher could call a different overload."
            ),
            _ when SynthesizedCompanionMembers.Contains(tsName) => Error(
                method,
                $"[JsonFactory] '{name}' emits as '{tsName}', which the branded companion already declares; rename it or add [Name]."
            ),
            _ => null,
        };
    }

    private static bool IsNotEmitted(IMethodSymbol method) =>
        method.HasIgnore(TargetLanguage.TypeScript) || method.IsInlinedAtCallSite();

    private static bool IsOverloaded(INamedTypeSymbol type, IMethodSymbol method) =>
        type.GetMembers(method.Name).OfType<IMethodSymbol>().Count(IsOrdinary) > 1;

    private static bool IsOrdinary(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary;

    private static bool TakesOnly(IMethodSymbol method, ITypeSymbol underlying) =>
        method.Parameters is [var parameter] && SameType(parameter.Type, underlying);

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
