using Metano.Interop;

namespace Conformance;

public class InteropPet
{
    public string Name => "rex";
}

/// <summary>
/// Spec 011 stage 011f: Js.Code inline TypeScript and the UnsafeCast / AsDynamic
/// escape hatches. Js.Code throws on .NET by design (FR-031), and UnsafeCast is
/// checked on .NET and unchecked in TypeScript (OD-15), so those rows are known gaps.
/// </summary>
public static class InteropProbes
{
    public static string UnsafeCastOk()
    {
        object pet = new InteropPet();
        return pet.UnsafeCast<InteropPet>().Name;
    }

    public static string UnsafeCastWrong()
    {
        object text = "text";

        try
        {
            var pet = text.UnsafeCast<InteropPet>();
            return pet is null ? "null" : "cast";
        }
        catch (InvalidCastException)
        {
            return "InvalidCastException";
        }
    }

    public static string AsDynamicIdentity()
    {
        var pet = new InteropPet();
        object seen = pet.AsDynamic();
        return ReferenceEquals(seen, pet) ? "same" : "different";
    }

    public static string InlineCodeExpression()
    {
        try
        {
            return Js.Code<int>("6 * 7").ToString();
        }
        catch (Exception)
        {
            return "threw";
        }
    }

    public static string InlineCodeHoles()
    {
        var a = 20;

        try
        {
            return Js.Code<string>($"String({a} + 22)");
        }
        catch (Exception)
        {
            return "threw";
        }
    }

    public static string InlineCodeStatement()
    {
        var box = new int[1];

        try
        {
            Js.Code($"{box}[0] = 5");
        }
        catch (Exception)
        {
            return "threw";
        }

        return box[0].ToString();
    }
}
