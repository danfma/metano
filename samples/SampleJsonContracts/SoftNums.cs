namespace SampleJsonContracts;

// Every member is nullable, so the partial Web payloads of the
// number-from-string rows read on both sides (a missing nullable member
// reads as null; the FR-045 missing-property throw only covers non-nullable
// members). Nums stays the full-payload contract (WEB3, WEB7).
public sealed record SoftNums(int? N, decimal? D, double? X, Color? C);
