namespace Conformance;

// These cross-type value references occur only inside static constructors.
// Runtime rows pin successful deferred initialization; TUnit separately asserts
// MS0005, which warns about the import cycle without rejecting these programs.
public class LzDiagnosticCallFirst
{
    public static int Value;

    static LzDiagnosticCallFirst()
    {
        Value = LzDiagnosticCallSecond.Read() + 1;
    }

    public static int Read() => Value;
}

public class LzDiagnosticCallSecond
{
    public static int Value;

    static LzDiagnosticCallSecond()
    {
        Value = LzDiagnosticCallFirst.Read() + 5;
    }

    public static int Read() => Value;
}

public class LzDiagnosticNewFirst
{
    public static int Value;

    static LzDiagnosticNewFirst()
    {
        var second = new LzDiagnosticNewSecond();
        Value = 3;
    }
}

public class LzDiagnosticNewSecond
{
    public static int Value;

    static LzDiagnosticNewSecond()
    {
        var first = new LzDiagnosticNewFirst();
        Value = 7;
    }
}

public static class LazyDiagnosticProbes
{
    public static string StaticConstructorOnlyMethodCycle() =>
        LzDiagnosticCallFirst.Read() + ":" + LzDiagnosticCallSecond.Read();

    public static string StaticConstructorOnlyCreationCycle() =>
        LzDiagnosticNewFirst.Value + ":" + LzDiagnosticNewSecond.Value;
}
