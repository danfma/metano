namespace Conformance;

public class Animal
{
    public virtual string Name => "animal";

    public Dog AsDog() => (Dog)this;
}

public class Dog : Animal
{
    public override string Name => "dog";

    public string Bark() => "woof";
}

public class Cat : Animal
{
    public override string Name => "cat";
}

public interface IWalker
{
    string Walk();
}

public class Robot : IWalker
{
    public string Walk() => "clank";
}

public record Rec(int A);

public record SubRec(int A, int B) : Rec(A);

public class GBox<T>
{
    public T Item = default!;
}

public static class CastProbes
{
    public static string DownOk()
    {
        Animal a = new Dog();
        return ((Dog)a).Bark();
    }

    public static string DownFail()
    {
        Animal a = new Cat();
        return ((Dog)a).Bark();
    }

    public static string DownNull()
    {
        Animal? a = null;
        var d = (Dog?)a;
        var e = (Dog)a!;
        return (d == null ? "null" : "x") + (e == null ? "null" : "x");
    }

    public static string FromObject()
    {
        object o = new Dog();
        return ((Animal)o).Name + ((Dog)o).Bark();
    }

    public static string ObjectToString()
    {
        object o = "s";
        return (string)o + ((string)o).Length;
    }

    public static string ObjectToStringFail()
    {
        object o = 5;
        return (string)o;
    }

    public static string Unbox()
    {
        object o = 5;
        object b = true;
        object d = 1.5;
        return ((int)o + 1) + "|" + ((bool)b ? "T" : "F") + "|" + ((double)d).ToString();
    }

    public static string UnboxLong()
    {
        object o = 5L;
        return ((long)o + 1).ToString();
    }

    public static string Interface()
    {
        object o = new Robot();
        return ((IWalker)o).Walk();
    }

    public static string InterfaceFail()
    {
        object o = new Dog();
        return ((IWalker)o).Walk();
    }

    public static string InterfaceFromClass()
    {
        Robot r = new Robot();
        IWalker w = r;
        return ((Robot)w).Walk() + ((IWalker)r).Walk();
    }

    public static string Upcast()
    {
        Dog d = new Dog();
        return ((Animal)d).Name + ((object)d != null ? "T" : "F");
    }

    public static string Generic()
    {
        return G<Dog>(new Dog()).Bark() + G<string>("s") + G<int>(5);
    }

    static T G<T>(object o) => (T)o;

    public static string GenericFail()
    {
        return G<Dog>(new Cat()).Bark();
    }

    public static string ThisCast()
    {
        Animal a = new Dog();
        return a.AsDog().Bark();
    }

    public static string ThisCastFail()
    {
        Animal a = new Cat();
        return a.AsDog().Bark();
    }

    public static string Records()
    {
        Rec r = new SubRec(1, 2);
        return ((SubRec)r).B.ToString();
    }

    public static string RecordsFail()
    {
        Rec r = new Rec(1);
        return ((SubRec)r).B.ToString();
    }

    public static string Collections()
    {
        object o = new List<int> { 1, 2 };
        IEnumerable<int> e = (IEnumerable<int>)o;
        var l = (List<int>)o;
        object arr = new[] { 1 };
        var a = (int[])arr;
        IReadOnlyList<int> ro = l;
        var back = (List<int>)ro;
        return l.Count + "|" + a.Length + "|" + e.Count() + "|" + back.Count;
    }

    public static string GenericClass()
    {
        object o = new GBox<int> { Item = 3 };
        return ((GBox<int>)o).Item.ToString();
    }

    public static string AsOp()
    {
        Animal a = new Cat();
        var d = a as Dog;
        var c = a as Cat;
        return (d == null ? "null" : "dog") + (c == null ? "null" : "cat");
    }

    public static string Exceptions()
    {
        Exception e = new InvalidOperationException("m");
        var i = (InvalidOperationException)e;
        return i.Message;
    }

    public static string ExceptionsFail()
    {
        Exception e = new ArgumentException("m");
        var i = (InvalidOperationException)e;
        return i.Message;
    }

    public static string InConditional()
    {
        Animal a = new Dog();
        var s = a is Dog ? ((Dog)a).Bark() : "no";
        return s;
    }

    public static string Delegates()
    {
        object o = (Func<int, int>)(x => x + 1);
        var f = (Func<int, int>)o;
        return f(1).ToString();
    }

    public static string Enums()
    {
        object o = Color.Green;
        var c = (Color)o;
        int i = (int)c;
        return c + "|" + i + "|" + (Color)1;
    }

    public static string Structs()
    {
        object o = new Pt(1, 2);
        var p = (Pt)o;
        return p.X + "|" + p.Y;
    }

    public static string Chained()
    {
        object o = new Dog();
        return ((Dog)(Animal)o).Bark();
    }

    public static string Lambda()
    {
        var xs = new List<Animal> { new Dog(), new Dog() };
        return string.Join(",", xs.Select(a => ((Dog)a).Bark()));
    }

    public static string CastOfT()
    {
        var xs = new List<Animal> { new Dog() };
        return xs.Cast<Dog>().First().Bark() + xs.OfType<Cat>().Count();
    }

    public static string FailMessage()
    {
        try
        {
            object o = new Cat();
            var d = (Dog)o;
            return "no";
        }
        catch (InvalidCastException e)
        {
            return "ICE";
        }
    }
}
