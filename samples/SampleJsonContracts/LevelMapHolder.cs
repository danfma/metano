namespace SampleJsonContracts;

// System.Text.Json writes an enum dictionary key as its member name, with or
// without JsonStringEnumConverter, and reads the name back (#479).
public sealed record LevelMapHolder(Dictionary<Level, string> ByLevel);
