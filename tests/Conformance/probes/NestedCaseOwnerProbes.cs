namespace Conformance;

/// <summary>An owner whose cases are nested records, read through its static getters (#726).</summary>
public abstract record ShotOrigin
{
    public sealed record Fresh : ShotOrigin
    {
        public static readonly Fresh Instance = new();
    }

    public sealed record Reused(int From) : ShotOrigin;
}

/// <summary>
/// Nested cases used as values from another module: each row needs the owner imported as a
/// value, not with <c>import type</c> (#726).
/// </summary>
public static class NestedCaseOwnerProbes
{
    public static string SwitchArm() =>
        Label(ShotOrigin.Fresh.Instance) + "|" + Label(new ShotOrigin.Reused(3));

    public static string IsPattern()
    {
        object fresh = ShotOrigin.Fresh.Instance;
        object reused = new ShotOrigin.Reused(7);

        return (fresh is ShotOrigin.Fresh) + "|" + (reused is ShotOrigin.Fresh);
    }

    private static string Label(ShotOrigin origin) =>
        origin switch
        {
            ShotOrigin.Fresh => "fresh",
            ShotOrigin.Reused reused => "reused " + reused.From,
            _ => "other",
        };
}
