using System;
using SampleIssueTracker.SharedKernel;

namespace SampleIssueTracker.Issues.Domain;

/// <summary>
/// One entry of an issue's activity feed: a snowflake id, the ticks the
/// entry happened at, and the ticks the action took (spec 012 FR-068). Both
/// 64-bit values flow through the generated <c>JsonContext</c> exactly:
/// ids above 2^53 keep every digit, and tick math wraps (or throws, under
/// <c>checked</c>) where C# wraps.
/// </summary>
public record IssueActivity(
    SnowflakeId Id,
    string IssueKey,
    IssueActivityKind Kind,
    long OccurredAtTicks,
    long? DurationTicks
)
{
    /// <summary>The ticks from this entry's timestamp to <paramref name="endTicks"/>.</summary>
    public long ElapsedTicksUntil(long endTicks) => endTicks - OccurredAtTicks;

    /// <summary>Whether the entry happened within the tick interval, bounds included.</summary>
    public bool OccurredWithin(long startTicks, long endTicks) =>
        OccurredAtTicks >= startTicks && OccurredAtTicks <= endTicks;

    /// <summary>
    /// The tick-based duration as a <see cref="TimeSpan"/>, round tripping
    /// through its ticks the way a duration column does.
    /// </summary>
    public TimeSpan Duration() => TimeSpan.FromTicks(DurationTicks ?? 0);
}
