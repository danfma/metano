namespace Metano.Annotations;

/// <summary>
/// Marks a parameter or field whose value must be a compile-time
/// constant literal. On a parameter, every argument passed at the
/// call site must resolve to a Roslyn <c>ConstantValue</c>; on a
/// field, the initializer expression must do the same. Violations
/// surface as <c>MS0014 InvalidConstant</c>.
/// <para>
/// The attribute exists so downstream lowering (<c>[Emit]</c>
/// templates with literal-only substitution, <c>[Inline]</c>
/// expansion that needs the caller's value in source form) can
/// rely on the value being known at compile time without a
/// separate analyzer pass.
/// </para>
/// <para>
/// Constant propagation follows the Roslyn contract: literal
/// tokens (<c>"div"</c>, <c>42</c>, <c>true</c>), <c>const</c> locals
/// and fields, and <c>readonly</c> fields whose initializer is
/// itself a constant are all accepted. Method calls, variables
/// without <c>const</c> or <c>readonly</c> modifiers, and any
/// expression whose value is not reducible by the C# compiler are
/// rejected.
/// </para>
/// <para>
/// <c>[Constant]</c> lives in <see cref="Metano.Annotations"/>
/// because the semantic (compile-time literal value) is meaningful
/// for every target; per-target consumers layer on top.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field, Inherited = false)]
public sealed class ConstantAttribute : Attribute;
