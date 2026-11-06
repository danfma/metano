using SampleIssueTracker.Issues.Domain;

namespace SampleIssueTracker.Issues.Application;

/// <summary>
/// The activity feed of an issue tracker: entries keyed by their snowflake
/// id (spec 012 FR-068). The key is a <c>long</c>, so the generated
/// dictionary holds <c>bigint</c> keys and the JSON root reads a property
/// name like <c>"1152921504606846977"</c> back as a <c>bigint</c>, never as
/// a rounded <c>number</c>.
/// </summary>
public class IssueActivityLog
{
    private readonly Dictionary<long, IssueActivity> _activities = [];

    public IReadOnlyDictionary<long, IssueActivity> Activities => _activities;

    public int Count => _activities.Count;

    public IssueActivity Record(IssueActivity activity)
    {
        _activities.Add(activity.Id.Value, activity);

        return activity;
    }

    public IssueActivity? Find(long id) =>
        _activities.TryGetValue(id, out var activity) ? activity : null;

    public bool Remove(long id) => _activities.Remove(id);

    /// <summary>
    /// The total ticks the recorded actions took. The checked 64-bit
    /// <c>Sum</c> throws <c>OverflowException</c> past <c>long.MaxValue</c>,
    /// as LINQ's checked sum does.
    /// </summary>
    public long TotalDurationTicks() =>
        _activities.Values.Sum(activity => activity.DurationTicks ?? 0);

    /// <summary>The entries that happened within the tick interval, in feed order.</summary>
    public IReadOnlyList<IssueActivity> Within(long startTicks, long endTicks) =>
        _activities
            .Values.Where(activity => activity.OccurredWithin(startTicks, endTicks))
            .OrderBy(activity => activity.OccurredAtTicks)
            .ToList();
}
