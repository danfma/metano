using Metano.Annotations;
using D = System.Decimal;
using Err = Conformance.Deep.Space.DeepError;
using G = System.Guid;
using Ids = System.Collections.Generic.List<int>;
using Pair = Conformance.Deep.Space.Pair<int, string>;
using Sb = System.Text.StringBuilder;
using Txt = Conformance.Deep.Space.Formatter;

namespace Conformance.Deep.Space
{
    public static class Formatter
    {
        public static string Wrap(string s) => "[" + s + "]";

        public static T Echo<T>(T x) => x;

        public static List<T> Many<T>(int n, Func<T> f)
        {
            var l = new List<T>();
            for (var i = 0; i < n; i++)
                l.Add(f());
            return l;
        }

        public static int Zero<T>() => 0;
    }

    public class Pair<A, B>
    {
        public A First;
        public B Second;

        public Pair(A a, B b)
        {
            First = a;
            Second = b;
        }

        public static Pair<A, B> Of(A a, B b) => new(a, b);
    }

    public class DeepError : Exception
    {
        public DeepError(string m)
            : base(m) { }
    }

    public class Maker
    {
        public int V = 4;
    }
}

namespace Conformance
{
    public static class AliasProbes
    {
        public static string StaticCall() => Txt.Wrap("x") + Txt.Echo<int>(5) + Txt.Echo("s");

        public static string GenericAlias()
        {
            var p = new Pair(1, "a");
            var q = Pair.Of(2, "b");
            return p.First + p.Second + q.First + q.Second;
        }

        public static string BclAlias()
        {
            var sb = new Sb();
            sb.Append("a").Append(1);
            var ids = new Ids { 1, 2 };
            return sb.ToString() + ids.Count;
        }

        public static string DecimalAlias()
        {
            D d = D.Parse("1.5");
            var z = D.Zero;
            var g = G.Empty;
            return (d + z) + "|" + g + "|" + D.Round(2.5m);
        }

        public static string CatchAlias()
        {
            try
            {
                throw new Err("deep");
            }
            catch (Err e)
            {
                return e.Message;
            }
        }

        public static string ExplicitTypeArgs()
        {
            return Txt.Many<Conformance.Deep.Space.Maker>(2, () =>
                    new Conformance.Deep.Space.Maker()
                ).Count
                + "|"
                + Txt.Zero<Conformance.Deep.Space.Maker>()
                + "|"
                + Txt.Many<object>(1, () => new Conformance.Deep.Space.Maker()).Count;
        }

        public static string IsAlias()
        {
            object o = new Err("x");
            object p = new Pair(1, "z");
            return (o is Err ? "T" : "F") + (p is Pair pp ? pp.Second : "F");
        }
    }
}
