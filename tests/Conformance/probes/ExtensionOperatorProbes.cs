using System.Numerics;
using Lib;

namespace Conformance;

public sealed class EoSig(int value)
{
    public int Value { get; } = value;
}

public readonly struct EoMoney(int cents)
{
    public int Cents { get; } = cents;
}

public sealed class EoBox<T>(T value)
{
    public T Value { get; } = value;
}

public static class EoSigOps
{
    extension(EoSig)
    {
        public static EoSig operator +(EoSig a, EoSig b) => new(a.Value + b.Value);

        public static EoSig operator -(EoSig a) => new(-a.Value);

        public static EoSig operator ++(EoSig a) => new(a.Value + 1);

        public static EoSig operator --(EoSig a) => new(a.Value - 1);

        public static bool operator <(EoSig a, EoSig b) => a.Value < b.Value;

        public static bool operator >(EoSig a, EoSig b) => a.Value > b.Value;

        public static EoSig operator *(EoSig a, int k) => new(a.Value * k);

        public static EoSig operator checked *(EoSig a, int k) => new(checked(a.Value * k));
    }
}

public static class EoMoneyOps
{
    extension(EoMoney)
    {
        [Metano.Annotations.Name("addMoney")]
        public static EoMoney operator +(EoMoney a, EoMoney b) => new(a.Cents + b.Cents);

        public static bool operator ==(EoMoney a, EoMoney b) => a.Cents == b.Cents;

        public static bool operator !=(EoMoney a, EoMoney b) => a.Cents != b.Cents;
    }
}

public static class EoBoxOps
{
    extension<T>(EoBox<T>)
        where T : IAdditionOperators<T, T, T>
    {
        [Metano.Annotations.Name("addBox")]
        public static EoBox<T> operator +(EoBox<T> a, EoBox<T> b) => new(a.Value + b.Value);
    }
}

/// <summary>
/// C# 14 extension operators (#761): each application calls the operator the extension block
/// declares, never the native JS operator.
/// </summary>
public static class ExtensionOperatorProbes
{
    public static string BinaryAndUnary()
    {
        var a = new EoSig(5);
        var b = new EoSig(7);
        var sum = a + b;
        var negated = -sum;

        return $"{sum.Value},{negated.Value},{a < b},{a > b}";
    }

    public static string CompoundAndIncrement()
    {
        var a = new EoSig(5);
        var c = a;
        c += a;
        var before = c++;
        ++c;
        c--;

        return $"{c.Value},{before.Value}";
    }

    public static string CheckedOperator()
    {
        var big = new EoSig(int.MaxValue);
        string text;

        try
        {
            text = "no:" + checked(big * 2).Value;
        }
        catch (OverflowException)
        {
            text = "overflow";
        }

        return $"{text},{unchecked(big * 2).Value}";
    }

    public static string LiftedEquality()
    {
        EoMoney? some = new EoMoney(7);
        EoMoney? none = null;
        var total = new EoMoney(3) + new EoMoney(4);

        return $"{total.Cents},{total == new EoMoney(7)},{some == none},{some != none},{some == total}";
    }

    public static string GenericBlock() => (new EoBox<int>(2) + new EoBox<int>(3)).Value.ToString();
}

/// <summary>
/// An extension operator of a referenced package (#761). Its own class: a file that read
/// <c>__add</c> from two extension classes would report MS0021.
/// </summary>
public static class ExtensionOperatorPackageProbes
{
    public static string CrossPackage() => (new LSig(4) + new LSig(6)).Value.ToString();
}
