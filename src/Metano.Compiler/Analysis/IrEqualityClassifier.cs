using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Single source of truth for the per-field equality strategy the
/// record-synthesis bridge picks (and the runtime-requirement scanner
/// mirrors). Keeping the decision in one place avoids the two callers
/// drifting and emitting an unused <c>valueEquals</c> import (or the
/// reverse — emitting the call without the import).
///
/// <para>
/// "Strict" means <c>===</c> on the TS side: the field is either a
/// real JS primitive, an enum / branded primitive that lowers to one,
/// a bare type parameter (no shape known at compile time), or a
/// reference-typed collection where C#'s
/// <c>EqualityComparer&lt;T&gt;.Default</c> would also fall back to
/// reference equality.
/// </para>
/// <para>
/// "Value" means <c>valueEquals(a, b)</c>: the field is a JS object
/// wrapper carrying its own <c>equals</c> contract — Decimal,
/// Temporal, the runtime UUID, nested transpiled records, plain
/// classes / structs the user authored, or tuples (which compare
/// element-wise on the C# side).
/// </para>
/// </summary>
public static class IrEqualityClassifier
{
    /// <summary>
    /// True when the field's lowered surface uses <c>===</c> in the
    /// synthesized record <c>equals</c> body. False routes through the
    /// runtime <c>valueEquals</c> helper.
    /// </summary>
    public static bool UseStrictEquality(IrTypeRef? type) =>
        type switch
        {
            null => true,
            IrPrimitiveTypeRef p => UseStrictEqualityForPrimitive(p.Primitive),
            IrTypeParameterRef => true,
            IrNullableTypeRef nullable => UseStrictEquality(nullable.Inner),
            IrNamedTypeRef named => UseStrictEqualityForNamed(named),
            // Collection-like / function-like / reference shapes —
            // C#'s default equality comparer falls back to reference
            // equality for these unless the user overrides it. Preserve
            // the same semantics here so a record carrying an
            // <c>int[]</c> / <c>List&lt;T&gt;</c> / <c>Func&lt;…&gt;</c>
            // does not silently start comparing structurally.
            IrArrayTypeRef => true,
            IrMapTypeRef => true,
            IrSetTypeRef => true,
            IrIterableTypeRef => true,
            IrGeneratorTypeRef => true,
            IrPromiseTypeRef => true,
            IrFunctionTypeRef => true,
            IrKeyValuePairTypeRef => true,
            IrGroupingTypeRef => true,
            // Tuples compare element-wise in C#; the runtime helper
            // walks them recursively, matching that contract.
            IrTupleTypeRef => false,
            // Unknown shape: be conservative and stick with === so we
            // never synthesize a more lenient comparison than C# would.
            _ => true,
        };

    /// <summary>
    /// JS treats some C# primitives as object values at runtime — the
    /// BCL mappings lower <c>Decimal</c> to <c>decimal.js</c>'s
    /// <c>Decimal</c> class, the date / time family to Temporal
    /// objects, and <c>Guid</c> to the runtime's <c>UUID</c> class.
    /// Those wrappers expose <c>equals</c> contracts and need to
    /// route through <c>valueEquals</c>. <c>Object</c> joins them so
    /// boxed value-equal instances compare as <c>object.Equals</c>
    /// would on the C# side. Everything else is a real JS primitive
    /// where <c>===</c> is correct and faster.
    /// </summary>
    private static bool UseStrictEqualityForPrimitive(IrPrimitive p) =>
        p switch
        {
            IrPrimitive.Decimal => false,
            IrPrimitive.Guid => false,
            IrPrimitive.DateTime => false,
            IrPrimitive.DateTimeOffset => false,
            IrPrimitive.DateOnly => false,
            IrPrimitive.TimeOnly => false,
            IrPrimitive.TimeSpan => false,
            IrPrimitive.Object => false,
            _ => true,
        };

    /// <summary>
    /// Named types lower to a real TS class / type alias / interface.
    /// Enums and branded primitives compare strictly because the
    /// runtime value is the underlying primitive; everything else
    /// (record / class / struct / interface / exception / delegate)
    /// gets routed through <c>valueEquals</c> so user types carrying
    /// their own <c>equals</c> contract win, and types without one
    /// fall back to structural compare via the runtime helper —
    /// matching what <c>object.Equals</c> dispatches to on the C#
    /// side.
    /// </summary>
    private static bool UseStrictEqualityForNamed(IrNamedTypeRef named) =>
        named.Semantics?.Kind
            is IrNamedTypeKind.StringEnum
                or IrNamedTypeKind.NumericEnum
                or IrNamedTypeKind.Branded;
}
