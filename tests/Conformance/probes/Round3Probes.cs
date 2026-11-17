using Metano.Annotations;

[assembly: MapMethod(
    typeof(Conformance.Tool),
    nameof(Conformance.Tool.Old),
    JsTemplate = "Tool.twice($0)"
)]
[assembly: MapMethod(
    typeof(Conformance.Tool),
    nameof(Conformance.Tool.New),
    JsTemplate = "$Type.twice($0)"
)]

namespace Conformance
{
    public class Tool
    {
        public static int Twice(int x) => x * 2;

        public static int Old(int x) => x * 2;

        public static int New(int x) => x * 2;
    }

    public static class Round3Probes
    {
        static decimal Dec(string s) => decimal.Parse(s);

        public static string LongToDouble()
        {
            long l = 5;
            double d = l;
            return "v"
                + d
                + "|"
                + (double)l
                + "|"
                + l / 2.0
                + "|"
                + (d + 0.5)
                + "|"
                + $"{(double)l}";
        }

        public static string ULongToDouble()
        {
            ulong u = 7;
            double d = u;
            uint w = 3;
            double e = w;
            return "v" + d + "|" + e + "|" + (float)u;
        }

        public static string LongToFloat()
        {
            long l = 5;
            float f = l;
            return "v" + f + "|" + (float)l;
        }

        public static string CharEnumToDouble()
        {
            char c = 'a';
            double d = c;
            double e = (double)Color.Green;
            return "v" + d + "|" + e;
        }

        public static string NullableLift()
        {
            int? ni = 3;
            double? nd = ni;
            long? nl = 4;
            double? ne = nl;
            return "v" + nd + "|" + ne;
        }

        public static string DecimalRoutes()
        {
            decimal m = Dec("2.5");
            return "v"
                + decimal.ToDouble(m)
                + "|"
                + (double)(m * 2)
                + "|"
                + (double)Math.Round(m)
                + "|"
                + (float)m;
        }

        public static string DecimalToDoubleMath()
        {
            decimal m = Dec("2.5");
            double d = (double)m;
            return (d * 2).ToString()
                + "|"
                + (d > 2 ? "T" : "F")
                + "|"
                + Math.Floor(d)
                + "|"
                + (d == 2.5 ? "T" : "F");
        }

        public static string TimeProps()
        {
            var t = TimeSpan.FromSeconds(90);
            var a = new DateTime(2024, 1, 2);
            var b = new DateTime(2024, 1, 1);
            return "v"
                + t.TotalSeconds
                + "|"
                + t.TotalMinutes
                + "|"
                + (a - b).TotalDays
                + "|"
                + (a - b).TotalHours;
        }

        public static string Containers()
        {
            var d = new Dictionary<string, double> { ["a"] = 1e-7 };
            var xs = new[] { 1e21 };
            var l = new List<double> { 0.1 };
            (double X, bool Y) t = (1e-7, true);
            return "v"
                + d["a"]
                + "|"
                + xs[0]
                + "|"
                + l[0]
                + "|"
                + t.X
                + t.Y
                + "|"
                + l.Sum()
                + "|"
                + xs.Max()
                + "|"
                + l.First();
        }

        public static string MathCalls()
        {
            return "v"
                + Math.Pow(10, 21)
                + "|"
                + Math.Abs(-1e-7)
                + "|"
                + Math.Max(1.5, 2)
                + "|"
                + Math.PI
                + "|"
                + double.MaxValue
                + "|"
                + Math.Min(1, 2)
                + "|"
                + Math.Round(2.5)
                + "|"
                + Math.Truncate(-1.5);
        }

        public static string IntAverage()
        {
            var xs = new[] { 1, 2 };
            var ds = new[] { Dec("1"), Dec("2") };
            return "v"
                + xs.Average()
                + "|"
                + ds.Average()
                + "|"
                + xs.Sum()
                + "|"
                + xs.Select(x => x / 2.0).Sum();
        }

        public static string TemplateOld() => Tool.Old(21).ToString();

        public static string TemplateNew() => Tool.New(21).ToString();

        public static string BigIntToDouble()
        {
            var b = new System.Numerics.BigInteger(5);
            double d = (double)b;
            return "v" + d;
        }
    }
}
