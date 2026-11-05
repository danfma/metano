namespace SampleJsonContracts;

public sealed record Timeline(string Name, Clip Main, IReadOnlyList<Clip> Clips);
