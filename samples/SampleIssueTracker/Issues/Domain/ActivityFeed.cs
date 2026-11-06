namespace SampleIssueTracker.Issues.Domain;

/// <summary>
/// The JSON projection of an activity feed (<c>Issues.Application.IssueActivityLog</c>):
/// the entries as a dictionary keyed by snowflake id (spec 012 FR-068). A
/// <c>long</c>-keyed dictionary cannot be a <c>[JsonSerializable]</c> root
/// (MS0035), so the wire shape wraps it in this record: on the wire the keys
/// are written as their digits, and reading turns each property name back
/// into a <c>bigint</c> key, never a rounded <c>number</c>.
/// </summary>
public record ActivityFeed(IReadOnlyDictionary<long, IssueActivity> Entries);
