using Metano.Annotations;

namespace SampleIssueTracker.SharedKernel;

/// <summary>
/// A 64-bit Snowflake-style id (spec 012 FR-068): an identity the width of a
/// <c>long</c>, generated far above 2^53, where a JS <c>number</c> would
/// silently lose digits. The generated TypeScript brands it as
/// <c>bigint &amp; { __brand: "SnowflakeId" }</c> and its JSON descriptor
/// reads and writes the exact digits.
/// </summary>
[Branded]
public readonly record struct SnowflakeId(long Value)
{
    /// <summary>
    /// The validating factory the generated <c>JsonContext</c> deserializes
    /// through instead of the non-validating <c>SnowflakeId.create</c>.
    /// </summary>
    [JsonFactory]
    public static SnowflakeId From(long value)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException("A snowflake id must be positive.");
        }

        return new SnowflakeId(value);
    }

    /// <summary>
    /// The timestamp of a Discord-style snowflake: the 42 bits above the
    /// worker and sequence fields, in milliseconds since the custom epoch.
    /// Unsigned shift on a <c>long</c>, so the wrap-free bits stay exact.
    /// A static member: a <c>[Branded]</c> wrapper carries no instance
    /// methods, and it declares exactly one public value member
    /// (<see cref="Value"/>).
    /// </summary>
    public static long TimestampMilliseconds(SnowflakeId id) => id.Value >>> 22;
}
