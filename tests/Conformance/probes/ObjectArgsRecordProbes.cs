using Metano.Annotations;

namespace Conformance
{
    /// <summary>A positional record whose primary constructor takes an object (#739).</summary>
    [method: ObjectArgs]
    public record OaPoint(int X, int Y = 2);

    /// <summary>A type-level [ObjectArgs] on a positional record (#739).</summary>
    [ObjectArgs]
    public record OaEntry(int A, string B = "x");

    /// <summary>A derived record over an [ObjectArgs] base (#739).</summary>
    [method: ObjectArgs]
    public record OaPoint3(int X, int Y = 2, int Z = 0) : OaPoint(X, Y);

    [method: ObjectArgs]
    public record OaTags(string Name, params string[] Items);

    public static class ObjectArgsRecordProbes
    {
        private static readonly List<string> OaLog = new();

        private static int OaL(string name, int value)
        {
            OaLog.Add(name);

            return value;
        }

        public static string NamedEvaluationOrder()
        {
            OaLog.Clear();

            var p = new OaPoint(Y: OaL("y", 2), X: OaL("x", 1));

            return $"{string.Join(",", OaLog)} {p.X} {p.Y}";
        }

        public static string NullParamsArray()
        {
            var t = new OaTags("a", null!);

            return $"{t.Items?.Length ?? 0}";
        }

        public static string ParamsExpanded()
        {
            var a = new OaTags("a", "x", "y");
            var b = new OaTags("b");
            var c = new OaTags("c", new[] { "z" });

            return $"{a.Items.Length}{b.Items.Length}{c.Items.Length} {string.Join("+", a.Items)}";
        }

        public static string DefaultsOmitted()
        {
            var p = new OaPoint(1);

            return $"{p.X} {p.Y}";
        }

        public static string NamedOutOfOrder()
        {
            var e = new OaEntry(B: "y", A: 1);

            return $"{e.A} {e.B}";
        }

        public static string EqualityAndHash()
        {
            var a = new OaPoint(1, 2);
            var b = new OaPoint(X: 1);

            return $"{a == b} {a.Equals(b)} {a.GetHashCode() == b.GetHashCode()}";
        }

        public static string WithKeepsRecordSemantics()
        {
            var a = new OaPoint(1, 2);
            var b = a with { X = 5 };

            return $"{b.X} {b.Y} {a.X} {a == b}";
        }

        public static string ToStringText() => new OaEntry(A: 3).ToString();

        public static string DerivedRecord()
        {
            var d = new OaPoint3(1, 2, 3);
            var e = new OaPoint3(1, 2);

            return $"{d} {e.Z} {d == e}";
        }
    }
}
