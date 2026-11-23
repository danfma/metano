namespace SampleJsonContracts;

public sealed record Gauge(int Level, string Unit) : IGauge;
