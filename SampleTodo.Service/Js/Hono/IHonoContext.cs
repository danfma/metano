using MetaSharp.Annotations;

namespace SampleTodo.Service.Js.Hono;

[NoEmit]
public interface IHonoContext
{
    [Name("text")]
    IHonoContext Text(string text);
}
