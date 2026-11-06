namespace SampleJsonContracts;

// The 64-bit integers of spec 012 (slice 012e). The manifest rows pin what
// System.Text.Json writes and reads for them; the TypeScript side runs the
// wire-compatible rows against the sample's generated contexts, which carry
// the 2.x number descriptors until the bigint flip (012k) turns them into
// the int64 descriptors the runtime already handles.
public sealed record Int64Box(long Id, ulong Mask, long? Parent);
