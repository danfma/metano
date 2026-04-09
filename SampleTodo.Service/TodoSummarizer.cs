using SampleTodo;

namespace SampleTodo.Service;

/// <summary>
/// Cross-package smoke test: a service-side helper that consumes types from the
/// sibling SampleTodo project. The transpiler should resolve TodoItem and Priority
/// to imports from the "sample-todo" npm package, and add the package as an entry
/// in the generated package.json#dependencies of the service.
/// </summary>
public class TodoSummarizer
{
    public string Describe(TodoItem item) =>
        $"{(item.Completed ? "[x]" : "[ ]")} {item.Title} ({item.Priority})";

    public TodoItem WithHighPriority(TodoItem item) =>
        item.SetPriority(Priority.High);
}
