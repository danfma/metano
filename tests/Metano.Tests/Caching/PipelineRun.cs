namespace Metano.Tests.Caching;

/// <summary>
/// The outcome of one host run in the cache tests: whether the host served it from the full
/// incremental cache (<see cref="CacheAdoptionRecorder.AdoptedCachedOutput"/>).
/// </summary>
internal sealed record PipelineRun(bool ServedFromCache);
