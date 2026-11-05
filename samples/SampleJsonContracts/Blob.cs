namespace SampleJsonContracts;

public sealed record Blob(byte[] Data, byte[]? Maybe, List<byte> Numbers);
