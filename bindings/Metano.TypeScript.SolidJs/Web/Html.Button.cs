using Metano.Annotations.TypeScript;

namespace Metano.TypeScript.SolidJs.Web;

public static partial class Html
{
    [JsxNativeElement("button")]
    public record Button : Element
    {
        public MouseClickHandler<Button>? OnClick { get; init; }
    }
}
