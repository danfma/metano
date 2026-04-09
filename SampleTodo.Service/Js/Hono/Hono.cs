using MetaSharp.Annotations;

namespace SampleTodo.Service.Js.Hono;

[Import(name: "Hono", from: "hono", Version = "^4.6.0")]
public class Hono
{
    public Hono() { }

    [Name("get")]
    public void Get(string path, Action<IHonoContext> handler) => throw new NotSupportedException();
}
