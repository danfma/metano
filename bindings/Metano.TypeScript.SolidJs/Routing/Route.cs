using Metano.Annotations;
using Metano.Annotations.TypeScript;

namespace Metano.TypeScript.SolidJs.Routing;

/// <summary>
/// A library-provided renderable from <c>solid-router</c>. It is not a Metano
/// component (it does not derive from the <c>[JsxComponentBuilder]</c> base) and
/// not a native intrinsic element; it is recognized purely as an imported
/// renderable: it derives from the marked <c>[External] JsxElement</c> and
/// carries <c>[Import("Route", from: "solid-router")]</c>. A
/// <c>new Route { Path = "/", Children = [...] }</c> therefore lowers to
/// <c>&lt;Route path="/"&gt;…&lt;/Route&gt;</c> with
/// <c>import { Route } from "solid-router"</c> (FR-022 / R7).
/// </summary>
[Import("Route", from: "solid-router", Version = "^0.15.0")]
public record Route : JsxElement
{
    /// <summary>The matched path pattern, emitted as the <c>path</c> attribute.</summary>
    public string? Path { get; init; }

    /// <summary>Nested renderables rendered inside the route.</summary>
    public JsxElement[]? Children { get; init; }
}
