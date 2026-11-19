namespace Conformance;

public enum BoundInt
{
    Zero,
    Max = int.MaxValue,
}

public enum BoundByte : byte
{
    Zero,
    Max = byte.MaxValue,
}

[Flags]
public enum BoundFlags
{
    Zero,
    Max = int.MaxValue,
}

public enum BoundLong : long
{
    Zero,
    Max = long.MaxValue,
}

public class BoundEnumBox
{
    public BoundInt Mode { get; set; }
    public BoundByte Narrow { get; set; }
    public BoundFlags Flags { get; set; }
    public BoundLong Wide { get; set; }
}

/// <summary>#708: bound compound stores and increments preserve types and evaluate once.</summary>
public static class BoundEnumStoreProbes
{
    private static BoundEnumBox Get(BoundEnumBox box, int[] calls)
    {
        calls[0]++;
        return box;
    }

    private static int Idx(int[] calls)
    {
        calls[0]++;
        return 0;
    }

    private static string IntStores(bool increment)
    {
        var box = new BoundEnumBox { Mode = BoundInt.Max };
        BoundEnumBox[] boxes = [box];
        var values = new BoundInt[1];
        values[0] = BoundInt.Max;
        var calls = new int[1];
        unchecked
        {
            if (increment)
            {
                boxes[0].Mode++;
                Get(box, calls).Mode++;
                values[Idx(calls)]++;
            }
            else
            {
                boxes[0].Mode += 1;
                Get(box, calls).Mode -= 2;
                values[Idx(calls)] += 1;
            }
        }
        return (int)box.Mode + "," + (int)values[0] + "," + calls[0];
    }

    private static string ByteStores(bool increment)
    {
        var box = new BoundEnumBox { Narrow = BoundByte.Max };
        BoundEnumBox[] boxes = [box];
        var values = new BoundByte[1];
        values[0] = BoundByte.Max;
        var calls = new int[1];
        unchecked
        {
            if (increment)
            {
                boxes[0].Narrow++;
                Get(box, calls).Narrow++;
                values[Idx(calls)]++;
            }
            else
            {
                boxes[0].Narrow += 1;
                Get(box, calls).Narrow -= 2;
                values[Idx(calls)] += 1;
            }
        }
        return (byte)box.Narrow + "," + (byte)values[0] + "," + calls[0];
    }

    private static string FlagsStores(bool increment)
    {
        var box = new BoundEnumBox { Flags = BoundFlags.Max };
        BoundEnumBox[] boxes = [box];
        var values = new BoundFlags[1];
        values[0] = BoundFlags.Max;
        var calls = new int[1];
        unchecked
        {
            if (increment)
            {
                boxes[0].Flags++;
                Get(box, calls).Flags++;
                values[Idx(calls)]++;
            }
            else
            {
                boxes[0].Flags += 1;
                Get(box, calls).Flags -= 2;
                values[Idx(calls)] += 1;
            }
        }
        return (int)box.Flags + "," + (int)values[0] + "," + calls[0];
    }

    private static string LongStores(bool increment)
    {
        var box = new BoundEnumBox { Wide = BoundLong.Max };
        BoundEnumBox[] boxes = [box];
        var values = new BoundLong[1];
        values[0] = BoundLong.Max;
        var calls = new int[1];
        unchecked
        {
            if (increment)
            {
                boxes[0].Wide++;
                Get(box, calls).Wide++;
                values[Idx(calls)]++;
            }
            else
            {
                boxes[0].Wide += 1;
                Get(box, calls).Wide -= 2;
                values[Idx(calls)] += 1;
            }
        }
        return (long)box.Wide + "," + (long)values[0] + "," + calls[0];
    }

    public static string IntCompound() => IntStores(false);

    public static string IntIncrement() => IntStores(true);

    public static string ByteCompound() => ByteStores(false);

    public static string ByteIncrement() => ByteStores(true);

    public static string FlagsCompound() => FlagsStores(false);

    public static string FlagsIncrement() => FlagsStores(true);

    public static string LongCompound() => LongStores(false);

    public static string LongIncrement() => LongStores(true);

    private static string CheckedIntStore(bool increment)
    {
        var box = new BoundEnumBox { Mode = BoundInt.Max };
        var calls = new int[1];
        try
        {
            checked
            {
                if (increment)
                    Get(box, calls).Mode++;
                else
                    Get(box, calls).Mode += 1;
            }
            return "no throw";
        }
        catch (OverflowException)
        {
            return (int)box.Mode + "," + calls[0];
        }
    }

    private static string CheckedByteStore(bool increment)
    {
        var box = new BoundEnumBox { Narrow = BoundByte.Max };
        var calls = new int[1];
        try
        {
            checked
            {
                if (increment)
                    Get(box, calls).Narrow++;
                else
                    Get(box, calls).Narrow += 1;
            }
            return "no throw";
        }
        catch (OverflowException)
        {
            return (byte)box.Narrow + "," + calls[0];
        }
    }

    private static string CheckedFlagsStore(bool increment)
    {
        var box = new BoundEnumBox { Flags = BoundFlags.Max };
        var calls = new int[1];
        try
        {
            checked
            {
                if (increment)
                    Get(box, calls).Flags++;
                else
                    Get(box, calls).Flags += 1;
            }
            return "no throw";
        }
        catch (OverflowException)
        {
            return (int)box.Flags + "," + calls[0];
        }
    }

    private static string CheckedLongStore(bool increment)
    {
        var box = new BoundEnumBox { Wide = BoundLong.Max };
        var calls = new int[1];
        try
        {
            checked
            {
                if (increment)
                    Get(box, calls).Wide++;
                else
                    Get(box, calls).Wide += 1;
            }
            return "no throw";
        }
        catch (OverflowException)
        {
            return (long)box.Wide + "," + calls[0];
        }
    }

    public static string IntCheckedCompound() => CheckedIntStore(false);

    public static string IntCheckedIncrement() => CheckedIntStore(true);

    public static string ByteCheckedCompound() => CheckedByteStore(false);

    public static string ByteCheckedIncrement() => CheckedByteStore(true);

    public static string FlagsCheckedCompound() => CheckedFlagsStore(false);

    public static string FlagsCheckedIncrement() => CheckedFlagsStore(true);

    public static string LongCheckedCompound() => CheckedLongStore(false);

    public static string LongCheckedIncrement() => CheckedLongStore(true);

    public static string BoundedLoop()
    {
        var count = 0;
        for (var e = BoundInt.Zero; e < (BoundInt)3; e++)
            count++;
        return count.ToString();
    }

    public static string CheckedBoundedLoop()
    {
        var count = 0;
        checked
        {
            for (var e = (BoundInt)2; e >= BoundInt.Zero; e--)
                count++;
        }
        return count.ToString();
    }
}
