using Metano.Annotations.TypeScript;

namespace Metano.TypeScript.SolidJs;

[JsxComponentBuilder]
public abstract record JsxComponent : IJsxComponentBuilder<JsxComponent, JsxElement>
{
    public abstract JsxElement Render();

    public static implicit operator JsxElement(JsxComponent component)
    {
        throw new NotImplementedException();
    }

    public static JsxElement Text(object? content) => throw new NotImplementedException();
}
