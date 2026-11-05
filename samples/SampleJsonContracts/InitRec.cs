namespace SampleJsonContracts;

public sealed record InitRec(int A)
{
    public string? Note { get; init; }
}
