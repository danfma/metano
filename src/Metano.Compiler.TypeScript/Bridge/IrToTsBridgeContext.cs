using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one context the TypeScript lowering bridges thread through a file group's
/// emission: the declarative BCL mappings (<see cref="Bcl"/>), the group's cast state
/// (<see cref="Casts"/>) and, inside a class emission, the per-class backing fields
/// of the class being emitted (<see cref="ClassFields"/> —
/// <see cref="PerClassBackingFieldScope.None"/> outside any class emission).
/// <para>
/// Replacing the registry + scope parameter pair with one object keeps every internal
/// bridge method explicit about the whole context, and the non-nullable scope makes
/// forgetting it a compile error rather than a silent no-scope lowering.
/// </para>
/// </summary>
/// <param name="Bcl">The declarative BCL mappings, or <c>null</c> to emit raw
/// member accesses and calls.</param>
/// <param name="ClassFields">The backing fields in scope; never <c>null</c>.</param>
/// <param name="TypeOverrides">The <c>[ExportFromBcl]</c> type overrides
/// (<c>decimal</c> → <c>Decimal</c>) the overload dispatchers apply to the
/// parameter types and runtime checks they emit, matching the class
/// emitter's own signatures. Required, so a class emission cannot forget it;
/// <c>null</c> (outside class emission, see <see cref="For"/>) keeps the
/// default mapping.</param>
internal sealed record IrToTsBridgeContext(
    DeclarativeMappingRegistry? Bcl,
    PerClassBackingFieldScope ClassFields,
    IrToTsTypeOverrides? TypeOverrides
)
{
    /// <summary>
    /// The cast state of the file group being transformed
    /// (<see cref="ReferenceCastLowering"/>): which classes its casts assert,
    /// and which local classes they check. <c>null</c> outside a group's
    /// transform, where every cast to a class is checked and none is recorded.
    /// </summary>
    public GroupCasts? Casts { get; private init; }

    /// <summary>
    /// The runtime type descriptors the transform emits (spec 011 D29), which
    /// every descriptor read the group renders is checked against;
    /// <see cref="Analysis.RuntimeTypeReachabilityResult.Empty"/> outside a
    /// group's transform.
    /// </summary>
    public Analysis.RuntimeTypeReachabilityResult RuntimeTypes { get; private init; } =
        Analysis.RuntimeTypeReachabilityResult.Empty;

    /// <summary>
    /// The active reified type-parameter scope for the member/constructor/body
    /// being lowered (spec 011 W5-5(g)).
    /// </summary>
    public TsTypeParameterScope TypeParameters { get; init; } = TsTypeParameterScope.Empty;

    public IrToTsBridgeContext WithTypeParameters(TsTypeParameterScope scope) =>
        this with
        {
            TypeParameters = scope,
        };

    /// <summary>
    /// Whether the class being emitted overrides <c>Message</c>, so its
    /// <c>base.Message</c> reads lower to the kept base message
    /// (<see cref="IrToTsExceptionConstructionBridge.TryMapKeptBaseMessageRead"/>).
    /// </summary>
    public bool KeepsBaseMessage { get; init; }

    /// <summary>
    /// The generated locals (<c>$</c>-names) the enclosing lowered loops
    /// declare, which no temporary of a nested statement list may reuse
    /// (<see cref="TsTemporaries"/>).
    /// </summary>
    public System.Collections.Immutable.ImmutableHashSet<string> ReservedLocals { get; init; } =
        System.Collections.Immutable.ImmutableHashSet<string>.Empty;

    /// <summary>
    /// The statement list being lowered, whose head declares the temporaries
    /// an expression in it needs (spec 011 contracts/runtime-collections.md §
    /// Indexer compound assignments); <c>null</c> outside a statement list (a
    /// field or static initializer, a constructor-initializer argument).
    /// </summary>
    public TsTemporaries? Temporaries { get; init; }

    /// <summary>
    /// What each bound current-element name of an enclosing
    /// <c>IrIndexerUpdateExpression</c> renders as (the element read, or the
    /// temporary that holds it).
    /// </summary>
    public System.Collections.Immutable.ImmutableDictionary<
        string,
        TsExpression
    > BoundElements { get; init; } =
        System.Collections.Immutable.ImmutableDictionary<string, TsExpression>.Empty;

    /// <summary>
    /// The parameter of the JS <c>catch</c> the position is lowered into, which
    /// a <c>throw;</c> rethrows; <c>null</c> outside a catch clause's body.
    /// </summary>
    public string? CaughtValue { get; private init; }

    /// <summary>
    /// The context of a catch clause's body, whose <c>throw;</c> statements
    /// rethrow <paramref name="caughtValue"/>: the parameter of the JS
    /// <c>catch</c> the clause lowers into.
    /// </summary>
    public IrToTsBridgeContext Rethrowing(string caughtValue) =>
        this with
        {
            CaughtValue = caughtValue,
        };

    /// <summary>
    /// The root context of one file group's transform: the context of a
    /// position outside class emission (module functions, plain objects,
    /// branded structs, static operators, JSX components, entry-point bodies),
    /// where no backing field is ever recognized and no type override applies,
    /// carrying the group's <paramref name="casts"/> and the transform's
    /// <paramref name="runtimeTypes"/>. A class emission derives
    /// its own context from it.
    /// </summary>
    public static IrToTsBridgeContext ForGroup(
        DeclarativeMappingRegistry? bclRegistry,
        GroupCasts casts,
        Analysis.RuntimeTypeReachabilityResult runtimeTypes
    ) => For(bclRegistry) with { Casts = casts, RuntimeTypes = runtimeTypes };

    /// <summary>
    /// A context outside any file group's transform: no backing field, no type
    /// override, no group cast state. Besides tests, it lowers only positions
    /// that cannot hold an explicit reference cast, so the missing
    /// <see cref="Casts"/> cannot change their output: the parameterless
    /// <c>new S()</c> of a struct's default value and the constant default of
    /// an <c>[ObjectArgs]</c> parameter. Every other position inside a group
    /// lowers through a context derived from <see cref="ForGroup"/>.
    /// </summary>
    public static IrToTsBridgeContext For(DeclarativeMappingRegistry? bclRegistry) =>
        new(bclRegistry, PerClassBackingFieldScope.None, TypeOverrides: null);
}
