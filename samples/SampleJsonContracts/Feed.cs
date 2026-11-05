namespace SampleJsonContracts;

public sealed record Feed(Page<EpisodeSummary> Latest);
