using Metano.Annotations;

namespace Conformance;

public interface IFwdGreeter
{
    [Name("hello")]
    string Greet(int n);
}

public class FwdGreeter : IFwdGreeter
{
    public string Greet(int n) => "int" + n;

    public string Greet(string s) => "str" + s;
}

public interface IFwdConv<T>
{
    string Conv(T x);

    string Conv(T x, int n);
}

public class FwdBase
{
    public virtual string Conv(int a) => "bi" + a;

    public virtual string Conv(int a, int n) => "bin" + a + n;
}

public class FwdDerived : FwdBase, IFwdConv<int> { }

public static class InterfaceForwarderProbes
{
    public static string RenamedSingleMember()
    {
        IFwdGreeter viaInterface = new FwdGreeter();
        var direct = new FwdGreeter();

        return viaInterface.Greet(1) + "," + direct.Greet(2) + "," + direct.Greet("x");
    }

    public static string InheritedImplementations()
    {
        IFwdConv<int> viaInterface = new FwdDerived();
        var direct = new FwdDerived();

        return viaInterface.Conv(1) + "," + viaInterface.Conv(1, 2) + "," + direct.Conv(3);
    }
}

public class MultiBase
{
    public virtual string Conv(int a) => "bi" + a;

    public virtual string Conv(int a, int n) => "bin" + a + n;

    public virtual string Conv(string a) => "bs" + a;

    public virtual string Conv(string a, int n) => "bsn" + a + n;
}

public class MultiDerived : MultiBase, IFwdConv<int>, IFwdConv<string>
{
    public override string Conv(int a) => "di" + a;
}

public static class MultiplyConstructedOverrideProbes
{
    public static string OverrideOfDispatchedBase()
    {
        var d = new MultiDerived();
        MultiBase b = d;
        IFwdConv<int> ci = d;
        IFwdConv<string> cs = d;

        return d.Conv(1)
            + ","
            + b.Conv(2)
            + ","
            + ci.Conv(3)
            + ","
            + cs.Conv("s")
            + ","
            + ci.Conv(4, 5)
            + ","
            + cs.Conv("t", 6);
    }
}
