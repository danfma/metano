namespace Metano.Annotations;

/// <summary>
/// Turns off the boxing of enum and <c>[InlineWrapper]</c> values at C# boxing
/// conversions for the whole assembly (spec 011 FR-050, OD-23): the value stays
/// raw where C# boxes it, so interop-heavy code pays no box, and a type question
/// (<c>GetType()</c>, <c>is</c>, <c>as</c>, a cast, a pattern) on an
/// <c>object</c>, <c>ValueType</c>, <c>Enum</c> or interface value that may hold
/// such a value answers from the raw value and reports <c>MS0058</c>. What a box
/// looks like is each target's own decision.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DisableBoxingAttribute : Attribute;
