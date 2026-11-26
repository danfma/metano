using Lib;

namespace Conformance;

public static class CrossPackageExtensionProbes
{
    public static string staticProperty()
    {
        var before = LCell.Origin.Value;
        LCell.Origin = LCell.Make(7);

        return $"{before},{LCell.Origin.Value}";
    }

    public static string instanceMembers()
    {
        var cell = new LCell(5);
        cell[1] = 11;

        return $"{cell.Doubled},{cell[2]},{cell.Plus(1)},{cell.Tripled()}";
    }
}
