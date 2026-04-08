using MetaSharp.Annotations;

namespace SampleTodo.Service.Js.Hono;

[Import(name: "Hono", from: "hono")]
public class Hono
{
    public Hono() { }

    [Name("get")]
    public void Get(string path, Action<IHonoContext> handler) => throw new NotSupportedException();
}

[NoEmit]
public interface IHonoContext
{
    [Name("text")]
    IHonoContext Text(string text);
}
