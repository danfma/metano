using System;
using System.Collections.Generic;

namespace Conformance;

public class DynBag
{
    public int count = 4;
}

public static class DynamicProbes
{
    public static string Arithmetic()
    {
        dynamic a = 2;
        dynamic b = 3;
        int sum = a + b;
        int product = a * b;

        return sum + "," + product;
    }

    public static string Concatenation()
    {
        dynamic s = "ab";
        string joined = s + "c";

        return joined;
    }

    public static string Indexer()
    {
        dynamic items = new int[] { 1, 2, 3 };
        int second = items[1];

        return second.ToString();
    }

    public static string DelegateInvoke()
    {
        dynamic next = (Func<int, int>)(x => x + 1);
        int three = next(2);

        return three.ToString();
    }

    public static string VerbatimMember()
    {
        dynamic bag = new DynBag();
        int before = bag.count;
        bag.count = 7;
        int after = bag.count;

        return before + "," + after;
    }

    public static string ImplicitString()
    {
        dynamic d = "x";
        string s = d;

        return s;
    }

    public static string ObjectToString()
    {
        dynamic d = 42;
        string text = d.ToString();

        return text;
    }

    public static string NumericCasts()
    {
        dynamic fraction = 3.7;
        dynamic negative = -3.7;
        dynamic big = 10L;
        dynamic small = 5;
        dynamic wide = 300;
        dynamic code = 97;
        dynamic money = 3.9m;
        int a = (int)fraction;
        int b = (int)negative;
        int c = (int)big;
        long d = (long)small;
        byte e = (byte)wide;
        char f = (char)code;
        int g = (int)money;
        decimal h = (decimal)small;
        double i = (double)big;

        return a + "," + b + "," + c + "," + d + "," + e + "," + f + "," + g + "," + h + "," + i;
    }

    public static string NullableCast()
    {
        dynamic none = null;
        dynamic seven = 7;
        int? a = (int?)none;
        int? b = (int?)seven;

        return (a == null ? "null" : "value") + "," + b;
    }

    public static string NumericCastFailures()
    {
        dynamic none = null;
        dynamic text = "42";
        dynamic flag = true;
        dynamic huge = 1e12m;
        dynamic wide = 300;

        return Threw(() => (int)none)
            + ","
            + Threw(() => (int)text)
            + ","
            + Threw(() => (int)flag)
            + ","
            + Threw(() => (int)huge)
            + ","
            + Threw(() => checked((byte)wide));
    }

    public static string ClassCasts()
    {
        dynamic dog = new Dog();
        dynamic cat = new Cat();
        Dog ok = (Dog)dog;

        return ok.Bark() + "," + Threw(() => (Dog)cat);
    }

    public static string Patterns()
    {
        dynamic dog = new Dog();
        dynamic text = "s";
        var isDog = dog is Dog ? "dog" : "other";

        string kind = text switch
        {
            int => "int",
            string => "string",
            _ => "other",
        };

        return isDog + "," + kind;
    }

    public static string NullConditional()
    {
        dynamic none = null;
        object read = none?.foo;

        return read == null ? "null" : "value";
    }

    public static string Foreach()
    {
        dynamic items = new List<int> { 1, 2 };
        var sum = 0;

        foreach (int item in items)
            sum += item;

        return sum.ToString();
    }

    public static string LateBoundSingle()
    {
        dynamic four = 4;
        int doubled = Twice(four);

        return doubled.ToString();
    }

    public static string LateBoundDispatcher()
    {
        dynamic text = "s";
        dynamic number = 3;
        string a = Kind(text);
        string b = Kind(number);

        return a + "," + b;
    }

    public static string MixedEquality()
    {
        dynamic one = 1;

        return Threw(() => one == "1");
    }

    public static string IntegerDivision()
    {
        dynamic seven = 7;
        int half = seven / 2;

        return half.ToString();
    }

    public static string MetanoMemberName()
    {
        dynamic dog = new Dog();
        string name = dog.Name;

        return name ?? "null";
    }

    private static int Twice(int x) => x * 2;

    private static string Kind(int x) => "int";

    private static string Kind(string s) => "string";

    private static string Threw(Func<object> action)
    {
        try
        {
            action();

            return "no";
        }
        catch (Exception)
        {
            return "threw";
        }
    }
}
