namespace Conformance.ExtOpsBarrel;

public sealed class BarrelSig(int value)
{
    public int Value { get; } = value;
}

public sealed class BarrelOther(int value)
{
    public int Value { get; } = value;
}

public static class BarrelSigOps
{
    extension(BarrelSig)
    {
        public static BarrelSig operator *(BarrelSig a, BarrelSig b) => new(a.Value * b.Value);
    }
}

public static class BarrelOtherOps
{
    extension(BarrelOther)
    {
        public static BarrelOther operator *(BarrelOther a, BarrelOther b) =>
            new(a.Value * b.Value + 1);
    }
}

/// <summary>
/// Two extension classes of one namespace that both declare <c>*</c> for their own receiver
/// (#761, decision W9-8): the namespace barrel exports each function under its receiver-qualified
/// name, so it loads (preloaded, see tools/preloads.txt).
/// </summary>
public static class ExtensionOperatorBarrelProbes
{
    public static string SameOperatorTwoReceivers() =>
        $"{(new BarrelSig(3) * new BarrelSig(4)).Value},{(new BarrelOther(3) * new BarrelOther(4)).Value}";
}
