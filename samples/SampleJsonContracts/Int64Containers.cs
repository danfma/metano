namespace SampleJsonContracts;

// The 64-bit containers of spec 012 (slice 012e): elements, dictionary keys
// and dictionary values. See Int64Box for the manifest's side split.
public sealed record Int64Containers(
    long[] Values,
    Dictionary<long, string> ByName,
    Dictionary<ulong, string> ByMask
);
