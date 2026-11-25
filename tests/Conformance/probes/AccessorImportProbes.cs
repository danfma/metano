namespace Conformance;

// Wave 9 group S: a type referenced only inside an accessor body is imported (#768).

public static class AiLog
{
    private static string _text = "";

    public static string Text => _text;

    public static void Write(string entry) => _text += entry + ";";

    public static void Reset() => _text = "";
}

public sealed class AiCell
{
    private static int _shared;
    private int _value;
    private readonly int _seed;

    public int Value
    {
        get => _value;
        set
        {
            _value = value;
            AiLog.Write("set " + value);
        }
    }

    public int Doubled
    {
        get => _value * 2;
        set => AiLog.Write("doubled " + value);
    }

    public int Seed
    {
        get => _seed;
        init
        {
            _seed = value;
            AiLog.Write("init " + value);
        }
    }

    public static int Shared
    {
        get => _shared;
        set
        {
            _shared = value;
            AiLog.Write("shared " + value);
        }
    }
}

public static class AccessorImportProbes
{
    public static string SetterBodiesReachTheirImports()
    {
        AiLog.Reset();

        var cell = new AiCell { Seed = 3 };
        cell.Value = 4;
        cell.Doubled = 5;
        AiCell.Shared = 6;

        return AiLog.Text + " value=" + cell.Value + " seed=" + cell.Seed;
    }
}
