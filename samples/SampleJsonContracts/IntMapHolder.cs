namespace SampleJsonContracts;

// Dictionary<int, string> cannot be a [JsonSerializable] root on the
// TypeScript target yet (MS0035, the non-string key rule), so the
// divergent integer-key wire-order row goes through a property instead.
public sealed record IntMapHolder(Dictionary<int, string> ByKey);
