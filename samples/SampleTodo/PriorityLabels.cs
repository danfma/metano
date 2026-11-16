namespace SampleTodo;

/// <summary>
/// Display labels per <see cref="Priority"/>, filled by the static constructor:
/// the <c>static readonly</c> table it writes stays assignable in the
/// generated <c>static { }</c> block.
/// </summary>
public static class PriorityLabels
{
    public static readonly Dictionary<Priority, string> Labels;

    static PriorityLabels()
    {
        Labels = new Dictionary<Priority, string>();
        Labels[Priority.Low] = "Low";
        Labels[Priority.Medium] = "Medium";
        Labels[Priority.High] = "High";
    }
}
