namespace SampleJsonContracts;

public sealed record HLeaf(int A, int B, int C) : HMid(A, B);
