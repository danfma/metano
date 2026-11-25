namespace Lib;

public sealed class LSig(int value)
{
    public int Value { get; } = value;
}

/// <summary>A C# 14 extension operator a referencing package applies (#761).</summary>
public static class LSigOps
{
    extension(LSig)
    {
        public static LSig operator +(LSig a, LSig b) => new(a.Value + b.Value);
    }
}
