namespace SampleJsonContracts;

// The temporal holder behind the #485 rows: one contract whose five temporal
// members carry every read position — System.Text.Json's accepted text
// variants, its rejected texts, and (through the #471 rows) null.
public record Schedule(
    DateTime At,
    TimeOnly Starts,
    DateOnly Day,
    TimeSpan Duration,
    DateTimeOffset Moment
);
