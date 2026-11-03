using Metano.Annotations;

namespace SampleIssueTracker.SharedKernel;

[Branded]
public readonly record struct UserId(string Value)
{
    public static UserId New() => new(Guid.NewGuid().ToString("N"));

    public static UserId System() => new("system");

    /// <summary>
    /// The validating factory the generated <c>JsonContext</c> deserializes through
    /// instead of the non-validating <c>UserId.create</c>.
    /// </summary>
    [JsonFactory]
    public static UserId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("A user id cannot be blank.");
        }

        return new UserId(value.Trim());
    }

    public override string ToString() => Value;
}
