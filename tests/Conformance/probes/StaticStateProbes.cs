namespace Conformance;

/// <summary>The side effects the static state of the classes below records.</summary>
public static class StaticStateLog
{
    public static readonly List<string> Entries = new();

    public static int Note(string entry)
    {
        Entries.Add(entry);

        return entry.Length;
    }
}

/// <summary>A static field initializer with an observable side effect, no static constructor.</summary>
public class Seeded
{
    public static readonly int Seed = StaticStateLog.Note("seed");

    public static readonly Seeded Instance = new();

    public int Read() => Seed;
}

/// <summary>Module state of a container-less static class (a module variable in TypeScript).</summary>
[Metano.Annotations.NoContainer]
public static class ModuleState
{
    public static int Hits = StaticStateLog.Note("hits");

    public static int Hit() => ++Hits;
}

/// <summary>
/// Static state is pure-annotated for bundlers (spec 011 FR-043); unbundled, its
/// initializers still run once, when the type is used.
/// </summary>
public static class StaticStateProbes
{
    public static string InitializersRunOnce()
    {
        var first = Seeded.Instance.Read();
        var second = Seeded.Seed;
        var hits = ModuleState.Hit() + ModuleState.Hit();
        var seeds = StaticStateLog.Entries.Count(entry => entry == "seed");
        var hitInits = StaticStateLog.Entries.Count(entry => entry == "hits");

        return first + "," + second + "," + hits + "," + seeds + "," + hitInits;
    }
}
