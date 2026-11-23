using System.Collections.Immutable;

namespace SampleJsonContracts;

// ImmutableArray<T> is a struct: System.Text.Json rejects JSON null for it (#713).
public sealed record ImmHolder(ImmutableArray<int> Items);
