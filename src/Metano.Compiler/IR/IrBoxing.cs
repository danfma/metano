namespace Metano.Compiler.IR;

/// <summary>
/// A C# boxing conversion of an enum or <c>[InlineWrapper]</c> value to
/// <c>object</c>, <c>ValueType</c> or <c>Enum</c> (spec 011 FR-050): Roslyn's
/// <c>Conversion.IsBoxing</c> over such a source, outside a generic
/// instantiation and an interop-bound <c>object</c> slot. Whether the value
/// carries its type at runtime (and how) is each target's decision; a target
/// without boxes reads <see cref="Value"/>.
/// </summary>
/// <param name="Value">The value converted.</param>
/// <param name="BoxedType">The enum or wrapper type boxed (never nullable).</param>
/// <param name="IsLifted">The source is a nullable of <see cref="BoxedType"/>, so
/// <c>null</c> converts to <c>null</c>.</param>
public sealed record IrBoxExpression(IrExpression Value, IrTypeRef BoxedType, bool IsLifted)
    : IrExpression;

/// <summary>
/// A value whose static type can hold a box (<c>object</c>, <c>ValueType</c>,
/// <c>Enum</c>, <c>dynamic</c>, an open type parameter) crossing into code that
/// reads it as a raw value (spec 011 FR-051): an argument of an
/// <c>[Import]</c> / <c>[Emit]</c> member, a <c>Js.Code</c> hole, a
/// <c>dynamic</c> operation, an <c>AsDynamic()</c> receiver. The unwrap is
/// shallow; a target without boxes reads <see cref="Value"/>.
/// </summary>
/// <param name="Value">The value crossing the boundary.</param>
/// <param name="Target">The static type of <see cref="Value"/>.</param>
/// <param name="Interop">The value crosses an interop boundary (always, today).</param>
public sealed record IrUnboxExpression(IrExpression Value, IrTypeRef Target, bool Interop)
    : IrExpression;

/// <summary>
/// An operand of a C# string concatenation (<c>"a" + o</c>) whose static type can
/// hold a box (<c>object</c>, <c>ValueType</c>, <c>Enum</c>, <c>dynamic</c>, a
/// type parameter, an interface): C# appends its <c>ToString()</c>, so a target
/// whose boxes carry their text renders that text (spec 011 FR-051); any other
/// target reads <see cref="Value"/>.
/// </summary>
/// <param name="Value">The operand.</param>
public sealed record IrConcatenatedValue(IrExpression Value) : IrExpression;
