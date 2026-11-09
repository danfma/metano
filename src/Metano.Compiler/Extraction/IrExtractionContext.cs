using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Everything an IR extractor needs beyond its syntax and semantic model:
/// the cross-package type-origin resolver, the compilation (null in
/// shape-only mode), the language target, and the target's local-naming
/// decision the local-name disambiguator maps bindings through (#398). One
/// immutable object threads through every extractor — type, member, module
/// function, statement, expression — replacing the
/// <c>originResolver / compilation / target / localNaming</c> parameter
/// clump, so a new extraction input reaches every nested extractor by
/// construction instead of by threading one more optional parameter.
/// </summary>
public sealed record IrExtractionContext(
    IrTypeOriginResolver? OriginResolver = null,
    Compilation? Compilation = null,
    Metano.Annotations.TargetLanguage? Target = null,
    LocalNaming? LocalNaming = null
)
{
    /// <summary>
    /// Whether the target lowers 64-bit integers exactly (spec 012): the
    /// extractor folds every 64-bit constant into one literal of its converted
    /// type and makes every conversion into or out of a 64-bit integer an
    /// <see cref="IR.IrNumericConversion"/>. Dart always sets it (DECIDED-19);
    /// TypeScript only in its staging <c>BigInt</c> mode, so its shipped
    /// <c>Number</c> mode keeps the 2.x IR (DECIDED-22). 012k makes it
    /// unconditional and removes the property.
    /// </summary>
    public bool ExactInt64Lowering { get; init; }

    /// <summary>
    /// The port through which a target says which types it emits with an
    /// addressable form per constructor overload — TypeScript a selector and,
    /// for a concrete type, a factory (spec 013 FR-007, FR-008, DECIDED-10),
    /// Dart a named constructor (FR-022, DECIDED-14) — so the extractor
    /// lowers calls into them to the overload Roslyn resolved
    /// (<see cref="DispatchedConstructorCalls"/>). <c>null</c>, the default,
    /// for a target that emits none: every construction and base call then
    /// keeps its source arguments and the target's dispatcher.
    /// </summary>
    public Func<INamedTypeSymbol, bool>? EmitsConstructorSelectors { get; init; }

    /// <summary>
    /// Whether the target emits an addressable fast path per overload of a
    /// dispatched operator group (spec 013 FR-033), so the extractor binds an
    /// operator application to the overload Roslyn resolved. TypeScript does
    /// (<c>Money.__addMoneyInt32(a, b)</c>); Dart declares one
    /// <c>operator +</c> per class and keeps the source operator, so it
    /// clears it. Method, <c>base.M</c>, method-group and module-function
    /// call sites bind on every target: each names the fast paths of those
    /// groups (#549).
    /// </summary>
    public bool EmitsOperatorFastPaths { get; init; } = true;

    /// <summary>
    /// The context of a constant parameter default: the origin resolver and
    /// the 64-bit lowering. It names no local, so it needs no local naming,
    /// and it drops the target and the compilation as defaults always have:
    /// a constant needs neither a target-specific rewrite (the Temporal
    /// comparisons) nor a second semantic model.
    /// </summary>
    public IrExtractionContext ForConstantDefault() =>
        new(OriginResolver) { ExactInt64Lowering = ExactInt64Lowering };

    /// <summary>Shape-only extraction: no resolver, compilation or target.</summary>
    public static IrExtractionContext Empty { get; } = new();
}
