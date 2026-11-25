namespace Lib;

public sealed class LCell(int value)
{
    public int Value { get; set; } = value;
}

public static class LCellDefaults
{
    public static LCell Origin { get; set; } = new(100);
}

/// <summary>C# 14 extension members a referencing package uses (#766).</summary>
public static class LCellExtensions
{
    extension(LCell)
    {
        public static LCell Origin
        {
            get => LCellDefaults.Origin;
            set => LCellDefaults.Origin = value;
        }

        public static LCell Make(int value) => new(value);
    }

    extension(LCell cell)
    {
        public int Doubled => cell.Value * 2;

        public int this[int offset]
        {
            get => cell.Value + offset;
            set => cell.Value = value - offset;
        }

        public int Plus(int other) => cell.Value + other;
    }

    public static int Tripled(this LCell cell) => cell.Value * 3;
}
