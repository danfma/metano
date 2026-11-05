namespace SampleJsonContracts;

public sealed record Group(string Name, IReadOnlyList<Clip> Members) : Clip(0);
