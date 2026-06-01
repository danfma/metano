using Metano.Annotations;
using Metano.TypeScript.DOM;

namespace Metano.TypeScript.SolidJs.Web;

[NoContainer]
public static class SolidRenderer
{
    [Import("render", from: "solid-js/web")]
    public static void Render(Func<JsxElement> render, HtmlElement container)
    {
        throw new NotImplementedException();
    }
}
