namespace Conformance;

public sealed class XiBag
{
    public List<int?> Slots { get; } = [1, null, 3];

    public int Writes { get; set; }

    public bool? Toggle { get; set; }

    public string? Text { get; set; }
}

public static class XiBagExtensions
{
    extension(XiBag bag)
    {
        public int? this[int index]
        {
            get => bag.Slots[index];
            set
            {
                bag.Writes++;
                bag.Slots[index] = value;
            }
        }

        public bool? Flag
        {
            get => bag.Toggle;
            set
            {
                bag.Writes++;
                bag.Toggle = value;
            }
        }

        public string? Label
        {
            get => bag.Text;
            set
            {
                bag.Writes++;
                bag.Text = value;
            }
        }
    }
}

public static class ExtensionIndexerProbes
{
    private static int _picks;

    private static int Pick(int index)
    {
        _picks++;

        return index;
    }

    private static string Show(int? value) => value?.ToString() ?? "null";

    private static string Show(bool? value) => value?.ToString() ?? "null";

    public static string readWrite()
    {
        var bag = new XiBag();
        bag[0] = 7;

        return $"{Show(bag[0])},{Show(bag[1])},{bag.Writes}";
    }

    public static string compoundLiftsNull()
    {
        var bag = new XiBag();
        bag[0] += 2;
        bag[1] += 2;
        bag[2]++;
        bag[1]--;

        return $"{Show(bag[0])},{Show(bag[1])},{Show(bag[2])}";
    }

    public static string indexEvaluatedOnce()
    {
        var bag = new XiBag();
        _picks = 0;
        bag[Pick(0)] += 10;
        bag[Pick(2)]++;

        return $"{Show(bag[0])},{Show(bag[2])},{_picks}";
    }

    public static string usedIncrement()
    {
        var bag = new XiBag();
        var post = bag[0]++;
        var pre = ++bag[0];
        var lifted = bag[1]++;

        return $"{Show(post)},{Show(pre)},{Show(lifted)},{Show(bag[0])}";
    }

    public static string usedAssignment()
    {
        var bag = new XiBag();
        var stored = bag[2] = 9;
        var compound = bag[2] += 1;

        return $"{Show(stored)},{Show(compound)},{Show(bag[2])}";
    }

    public static string coalesceStoresOnlyWhenNull()
    {
        var bag = new XiBag();
        bag[0] ??= 50;
        bag[1] ??= 60;
        var kept = bag[2] ??= 70;
        bag.Label ??= "first";
        bag.Label ??= "second";

        return $"{Show(bag[0])},{Show(bag[1])},{Show(kept)},{bag.Label},{bag.Writes}";
    }

    public static string nullableBooleanCompound()
    {
        var bag = new XiBag();
        bag.Flag &= false;
        var afterAnd = bag.Flag;
        bag.Flag = null;
        bag.Flag |= true;
        var afterOr = bag.Flag;
        bag.Flag = null;
        bag.Flag &= true;

        return $"{Show(afterAnd)},{Show(afterOr)},{Show(bag.Flag)}";
    }
}
