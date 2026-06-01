namespace Metano.Annotations.TypeScript;

/// <summary>
/// Marks the abstract base of a JSX component family. The transpiler keys off
/// this marker to recognize a JSX backend's component root: a concrete type
/// deriving (transitively) from a <c>[JsxComponentBuilder]</c>-marked base is
/// emitted as a function component (<c>export function Counter(props) { … }</c>),
/// while the marked base itself is never emitted (it is the family carrier, not
/// a component).
/// <para>
/// Carry the marker on the base only — never on the concrete components — so a
/// per-type annotation is not required. The base typically also implements
/// <see cref="IJsxComponentBuilder{TSelf, TElement}"/> to declare its render
/// method and the implicit conversion to the renderable element type.
/// </para>
/// <example>
/// <code>
/// [JsxComponentBuilder]
/// public abstract record JsxComponent : IJsxComponentBuilder&lt;JsxComponent, JsxElement&gt;
/// {
///     public abstract JsxElement Render();
///     public static implicit operator JsxElement(JsxComponent component) =&gt; throw null!;
/// }
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class JsxComponentBuilderAttribute : Attribute;

/// <summary>
/// The contract a JSX component family's abstract base implements so its
/// concrete components compile in C#.
/// <para>
/// <typeparamref name="TElement"/> is the marker the transpiler keys on: it is
/// the renderable element type (e.g. <c>JsxElement</c>) that a component renders
/// to and is convertible to. The transpiler decides "this lowers to JSX" off
/// the constructed type's renderability, not off observing the conversion.
/// </para>
/// <para>
/// The <c>static abstract implicit operator TElement(TSelf)</c> exists purely so
/// a component value can sit in a <typeparamref name="TElement"/>-typed position
/// in the C# source (e.g. a <c>JsxElement[] Children</c> slot). This lets the
/// MS0026 validator check a renderable position's converted type against the
/// marked element type without the conversion surviving into the output — the
/// operator is a compile-time affordance only and is never emitted.
/// </para>
/// </summary>
[External]
public interface IJsxComponentBuilder<in TSelf, out TElement>
    where TSelf : IJsxComponentBuilder<TSelf, TElement>
    where TElement : class
{
    TElement Render();

    static abstract implicit operator TElement(TSelf component);
}

/// <summary>
/// Marks a record/class as a native (intrinsic) JSX element. A
/// <c>new T { … }</c> of such a type lowers to the declared lowercase tag
/// (<c>new Html.Div { … }</c> → <c>&lt;div … /&gt;</c>) rather than a component
/// reference. The native tag wins over the component classification — an element
/// type may also derive from a <c>[JsxComponentBuilder]</c> base yet must still
/// render as its intrinsic tag.
/// <example>
/// <code>
/// [JsxNativeElement("div")]
/// public sealed record Div : Html.Node;
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class JsxNativeElementAttribute(string elementName) : Attribute
{
    /// <summary>The intrinsic JSX tag emitted for this element (e.g. <c>div</c>).</summary>
    public string ElementName { get; } = elementName;
}
