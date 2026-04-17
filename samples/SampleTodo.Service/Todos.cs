using Metano.Annotations;

namespace SampleTodo.Service;

/// <summary>
/// Wire shape for an existing todo as the service exposes it: a stable id plus the
/// underlying TodoItem from the sample-todo package. <c>[PlainObject]</c> ensures
/// this round-trips through JSON without a class wrapper.
/// </summary>
[PlainObject, EmitInFile("todos")]
public record StoredTodo(string Id, TodoItem Item);

/// <summary>
/// POST /todos request body. Plain shape; no methods, no equality.
/// </summary>
[PlainObject, EmitInFile("todos")]
public record CreateTodoDto(string Title, Priority Priority);

/// <summary>
/// PATCH /todos/:id request body. All fields nullable so partial updates work.
/// </summary>
[PlainObject, EmitInFile("todos")]
public record UpdateTodoDto(string? Title, Priority? Priority, bool? Completed);

/// <summary>
/// In-memory store of todos. Backed by a <see cref="List{T}"/> with linear scans —
/// fine for the sample's scale. We deliberately avoid <see cref="Dictionary{TKey,TValue}"/>
/// because the transpiler's Dictionary→Map mapping doesn't yet lower the indexer
/// (<c>dict[key]</c>) or <c>TryGetValue</c>; the existing
/// <see cref="SampleIssueTracker.Issues.Application.InMemoryIssueRepository"/> uses the
/// same List+FindIndex pattern. The point of the sample is to verify cross-project
/// types (TodoItem, Priority from sample-todo) flow through transpilation cleanly,
/// not to model a production app.
/// </summary>
[EmitInFile("todos")]
public class TodoStore
{
    private readonly List<StoredTodo> _items = [];

    public IReadOnlyList<StoredTodo> All() => _items.OrderBy(t => t.Id).ToList();

    public StoredTodo? Get(string id) => _items.FirstOrDefault(t => t.Id == id);

    public StoredTodo Add(CreateTodoDto dto)
    {
        var id = Guid.NewGuid().ToString();
        var stored = new StoredTodo(id, new TodoItem(dto.Title, false, dto.Priority));
        _items.Add(stored);
        return stored;
    }

    public StoredTodo? Update(string id, UpdateTodoDto patch)
    {
        // Find returns the item directly (or null), so the read is type-safe under
        // noUncheckedIndexedAccess. For the in-place write we look the index up
        // separately via IndexOf — reference equality, the item is the same object
        // we just retrieved so it always succeeds.
        var existing = _items.Find(t => t.Id == id);
        if (existing is null)
            return null;

        var item = existing.Item;
        if (patch.Title is not null)
            item = item with { Title = patch.Title };
        if (patch.Priority is not null)
            item = item with { Priority = patch.Priority.Value };
        if (patch.Completed is not null)
            item = item with { Completed = patch.Completed.Value };

        var updated = existing with { Item = item };
        _items[_items.IndexOf(existing)] = updated;
        return updated;
    }

    public bool Remove(string id)
    {
        var existing = _items.Find(t => t.Id == id);
        if (existing is null)
            return false;
        _items.Remove(existing);
        return true;
    }
}
