using MetaSharp.Annotations;
using SampleTodo.Service.Js.Hono;

[ExportedAsModule]
public static class Program
{
    [ModuleEntryPoint, ExportVarFromBody("app", AsDefault = true, InPlace = false)]
    public static void Main()
    {
        var app = new Hono();

        app.Get("/", c => c.Text("Hello Hono!!"));
    }
}
