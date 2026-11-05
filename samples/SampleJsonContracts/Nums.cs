namespace SampleJsonContracts;

public sealed record Nums(
    int N,
    int? M,
    decimal D,
    double X,
    List<int> L,
    Dictionary<string, int> Map,
    Color C
);
