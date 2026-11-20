using Metano.Annotations;

[assembly: MapMethod(typeof(Conformance.LzMapOnClass), "Compute", JsMethod = "computeImpl")]

namespace Conformance
{
    public record LzRecNullUndef(int V)
    {
        static LzRecNullUndef() => LazyLog.Write("cctor-rec-undef");
    }

    [PlainObject]
    public class LzPlainHolder
    {
        public LzRecNullUndef? Item { get; set; }
    }

    public class LzMapOnClass
    {
        static LzMapOnClass() => LazyLog.Write("cctor-map-on-class");

        public static int Compute(int x) => x + 1;

        public static int ComputeImpl(int x) => x + 1;
    }

    public struct LzPlainAutoPropStruct
    {
        public int P { get; set; }

        static LzPlainAutoPropStruct() => LazyLog.Write("cctor-plain-autoprop");
    }

    public static class LazyRoundOneProbes
    {
        public static string PlainObjectUndefinedEqualNull()
        {
            var holder = new LzPlainHolder();
            var equal = holder.Item == null;
            var notEqual = holder.Item != null;
            return LazyLog.Take("eq=" + equal + ":neq=" + notEqual);
        }

        public static string MapMethodOnClass()
        {
            var value = LzMapOnClass.Compute(3);
            return LazyLog.Take("v=" + value);
        }

        public static string PlainStructEquals()
        {
            var left = default(LzPlainAutoPropStruct);
            var right = default(LzPlainAutoPropStruct);
            LazyLog.Write("built");
            return LazyLog.Take("eq=" + left.Equals(right));
        }

        public static string PlainStructHashCode()
        {
            var value = default(LzPlainAutoPropStruct);
            LazyLog.Write("built");
            var hash = value.GetHashCode();
            return LazyLog.Take("ok");
        }
    }
}

namespace Conformance
{
    using Other = Conformance.LzAliasBystander;
    using Short = Conformance.LzAliasedOwner;

    public class LzAliasedOwner
    {
        static LzAliasedOwner() => LazyLog.Write("cctor-aliased-owner");

        [Inline]
        public static int Ping() => 1;
    }

    public class LzAliasBystander
    {
        static LzAliasBystander() => LazyLog.Write("cctor-bystander-WRONG");
    }

    public static class LazyAliasShadowProbes
    {
        public static string CallThroughAlias()
        {
            var value = Short.Ping();
            var name = typeof(Other).Name;
            return LazyLog.Take("v=" + value);
        }
    }
}
