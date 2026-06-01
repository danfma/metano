using Metano.TypeScript.SolidJs;
using Metano.TypeScript.SolidJs.Web;

namespace SampleSolidUi.Ui;

/// <summary>
/// Exercises the SolidJS <c>&lt;For&gt;</c> helper with a two-parameter render
/// lambda fed a materialized list. Proves the type-soundness of the lowered
/// <c>&lt;For&gt;</c> (fix #1): the render-prop params carry NO type annotation
/// (so Solid contextually types <c>item</c> as the element type and <c>index</c>
/// as <c>Accessor&lt;number&gt;</c> — a C# <c>int</c> annotation would clash),
/// and <c>each</c> is a real array (<c>number[]</c>), not a lazy
/// <c>Iterable&lt;T&gt;</c>. The generated <c>.tsx</c> is gated by the sample's
/// <c>tsgo -b</c> build. The <c>index</c> accessor is intentionally NOT read in
/// a value position: Solid passes it as <c>Accessor&lt;number&gt;</c> and the
/// index-accessor adaptation (calling <c>index()</c>) is deferred per R5/D7.
/// </summary>
public sealed record CounterList : JsxComponent
{
    public IReadOnlyList<int> Seeds { get; init; } = [];

    public override JsxElement Render() =>
        new Html.Div
        {
            ClassName = "counter-list",
            Children = [Solid.For(Seeds, (item, index) => new Counter { Count = item })],
        };
}
