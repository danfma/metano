namespace Metano.Annotations;

/// <summary>
/// Emits raw JavaScript at the call site. Use $0, $1, etc. for argument
/// placeholders. On a property the template lowers the read site: <c>$0</c>
/// is the receiver (e.g. <c>[Emit("$0[0]()")]</c> on a facade getter →
/// <c>receiver[0]()</c>). <c>$Type</c> stands for the type the method is declared
/// on, by its own exported name (never a <c>using</c> alias of it), and imports it:
/// <c>[Emit("$Type.make($0)")]</c> on <c>Fmt.Make</c> prints <c>Fmt.make(1)</c> and
/// imports <c>Fmt</c>. A type name written in the template text is not imported.
/// </summary>
/// <example>
/// [Emit("$0.toFixed($1)")]
/// public static extern string ToFixed(decimal value, int digits);
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class EmitAttribute(string expression) : Attribute
{
    public string Expression { get; } = expression;
}
