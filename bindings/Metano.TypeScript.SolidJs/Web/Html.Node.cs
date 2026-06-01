using Metano.Annotations.TypeScript;

namespace Metano.TypeScript.SolidJs.Web;

public static partial class Html
{
    [External]
    public abstract record Node : JsxComponent
    {
        public JsxElement[]? Children { get; init; }

        public override JsxElement Render() => throw new NotImplementedException();
    }
}
