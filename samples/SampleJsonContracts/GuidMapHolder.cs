namespace SampleJsonContracts;

// A Guid dictionary key round-trips in the "D" format (#479).
public sealed record GuidMapHolder(Dictionary<Guid, string> ById);
