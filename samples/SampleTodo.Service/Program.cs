using Metano.Annotations;
using SampleTodo.Service;
using SampleTodo.Service.Js.Hono;

[Erasable]
public static class Program
{
    [ModuleEntryPoint, ExportVarFromBody("app", AsDefault = true, InPlace = false)]
    public static void Main()
    {
        var app = new Hono();
        var store = new TodoStore();

        // Health check / greeting.
        app.Get("/", c => c.Text("sample-todo-service"));

        // List all todos.
        app.Get("/todos", c => c.Json(store.All()));

        // Get a single todo by id, or 404.
        app.Get(
            "/todos/:id",
            c =>
            {
                var id = c.Req.Param("id");
                if (id is null)
                    return c.NotFound();
                var stored = store.Get(id);
                return stored is null ? c.NotFound() : c.Json(stored);
            }
        );

        // Create a todo from a CreateTodoDto JSON body. Returns 201 + the stored item.
        app.Post(
            "/todos",
            async c =>
            {
                var dto = await c.Req.Json<CreateTodoDto>();
                var created = store.Add(dto);
                return c.Json(created, 201);
            }
        );

        // Patch (partial update). Returns the updated item or 404 when missing.
        app.Patch(
            "/todos/:id",
            async c =>
            {
                var id = c.Req.Param("id");
                if (id is null)
                    return c.NotFound();
                var patch = await c.Req.Json<UpdateTodoDto>();
                var updated = store.Update(id, patch);
                return updated is null ? c.NotFound() : c.Json(updated);
            }
        );

        // Delete by id. 204 No Content on success, 404 when missing. The Body()
        // overload exists precisely for status codes Hono won't accept via Text()
        // (204/205/304); without it the previous version had to fake a JSON envelope.
        app.Delete(
            "/todos/:id",
            c =>
            {
                var id = c.Req.Param("id");
                if (id is null)
                    return c.NotFound();
                return store.Remove(id) ? c.Body(null, 204) : c.NotFound();
            }
        );
    }
}
