using SampleIssueTracker.SharedKernel;

namespace SampleIssueTracker.Issues.Domain;

/// <summary>
/// Flat JSON projection of an <see cref="Issue"/> plus its comments, designed to
/// round-trip through Metano's <c>JsonSerializer</c>.
///
/// <para>
/// Lives alongside the aggregate in the domain package so the wire shape stays
/// close to its source of truth, but is kept as a separate type because
/// <see cref="Issue"/> itself has encapsulated state (private setters,
/// state-mutating methods) that does not round-trip cleanly from a flat factory
/// constructor. This is the canonical "snapshot for serialization" pattern —
/// rich aggregates expose a <c>ToSnapshot()</c> / <c>FromSnapshot()</c> pair and
/// the wire format operates on the snapshot, not the aggregate itself.
/// </para>
///
/// <para>
/// Also the only type in the sample that carries a <see cref="decimal"/>, which
/// exists so the Phase 5 serialization tests can exercise the custom-converter
/// code path (decimal → JSON number by default, decimal → JSON string when a
/// context-level converter is registered).
/// </para>
/// </summary>
public record IssueSnapshot(
    IssueId Id,
    string Title,
    string Description,
    IssueType Type,
    IssuePriority Priority,
    IssueStatus Status,
    UserId? AssigneeId,
    string? SprintKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    decimal EstimatedHours,
    Comment[] Comments
);
