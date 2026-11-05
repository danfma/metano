namespace SampleJsonContracts;

public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);
