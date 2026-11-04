namespace SampleOperatorOverloading;

/// <summary>
/// Overload dispatch with defaults and <c>params</c> arrays: the generated
/// constructor and methods take a call that omits defaulted arguments, and a
/// <c>params</c> overload takes its trailing arguments one by one.
/// </summary>
public class PriceRule
{
    public PriceRule(int percent = 10)
    {
        Description = $"{percent}% off";
    }

    public PriceRule(string code, params int[] tiers)
    {
        Description = $"{code} with {tiers.Length} tiers";
    }

    public string Description { get; }

    public string Label(Currency currency = Currency.Usd) =>
        currency == Currency.Usd ? $"{Description} in dollars" : $"{Description} abroad";

    public string Label(bool upper) => upper ? Description.ToUpper() : Description;

    public static string Threshold(int units, decimal minimum = 1.5m) =>
        $"{units} units over {minimum}";

    public static string Threshold(string code) => $"code {code}";

    public static int Sum(params int[] amounts)
    {
        var total = 0;

        foreach (var amount in amounts)
            total += amount;

        return total;
    }

    public static int Sum(string label, int amount) => amount;
}
