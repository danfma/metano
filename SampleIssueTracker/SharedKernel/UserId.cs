using MetaSharp;

namespace SampleIssueTracker.SharedKernel;

[InlineWrapper]
public readonly record struct UserId(string Value)
{
    public static UserId New() => new(Guid.NewGuid().ToString("N"));

    public static UserId System() => new("system");

    public override string ToString() => Value;
}
