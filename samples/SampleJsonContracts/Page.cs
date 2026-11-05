namespace SampleJsonContracts;

public sealed record Page<T>(IReadOnlyList<T> Items);
