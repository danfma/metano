namespace SampleJsonContracts;

public sealed record NLeaf(int A, int B, int C) : NMid(A, B);
