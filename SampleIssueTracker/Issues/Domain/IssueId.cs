using MetaSharp;

namespace SampleIssueTracker.Issues.Domain;

[InlineWrapper]
public readonly record struct IssueId(string Value)
{
    public static IssueId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
