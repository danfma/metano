using Metano.Annotations;

namespace SampleIssueTracker.Issues.Domain;

[StringEnum]
public enum IssueActivityKind
{
    [Name("created")]
    Created,

    [Name("assigned")]
    Assigned,

    [Name("status-changed")]
    StatusChanged,

    [Name("commented")]
    Commented,

    [Name("closed")]
    Closed,
}
