using Metano.Annotations;
using HTagAlias = Conformance.HTag;

namespace Conformance
{
    /// <summary>A private helper the module's public function calls (#609).</summary>
    [NoContainer]
    public static class HHelpers
    {
        private static long HTwice(long l) => l * 2;

        public static long HUseTwice(long l) => HTwice(l);
    }

    /// <summary>A static class emitted as a module because it holds an extension (#554).</summary>
    public static class HExtHost
    {
        public static int HSquared(this int x) => HSquare(x);

        private static int HSquare(int x) => x * x;

        public static int HPlain(int x) => x + 1;

        public static int HPlain(string s) => s.Length;
    }

    /// <summary>An overloaded group whose fast-path name a parameter spells (#578).</summary>
    [NoContainer]
    public static class HShadowed
    {
        public static int HDouble(int x) => x * 2;

        public static int HDouble(string s) => s.Length * 2;
    }

    /// <summary>A containerless function calling an [ObjectArgs] sibling (#405).</summary>
    [NoContainer]
    public static class HColumns
    {
        [ObjectArgs]
        public static int HColumn(int of = 0, int width = 100) => of + width;

        public static int HUseColumn() => HColumn(of: 3, width: 1);
    }

    /// <summary>A module variable reading a constant declared below it (#400).</summary>
    [NoContainer]
    public static class HLimits
    {
        public static readonly int HDerived = HMaxItems + 1;
        public const int HMaxItems = 3;
    }

    /// <summary>A type referenced through a `using` alias in its own file (#635).</summary>
    public class HTag
    {
        public HTag(int v) => V = v;

        public int V { get; }

        public static HTag From(int v) => new(v * 10);
    }

    /// <summary>An owner renamed to a lowercase name whose nested case a pattern tests.</summary>
    [Name("hShape")]
    public abstract record HShape
    {
        public sealed record HCircle(int R) : HShape;

        public sealed record HSquare(int S) : HShape;
    }

    public static class ModuleImportProbes
    {
        public static string PrivateModuleHelper() => HHelpers.HUseTwice(21).ToString();

        public static string ExtensionContainerCalls() =>
            $"{3.HSquared()} {HExtHost.HPlain(1)} {HExtHost.HPlain("abc")}";

        public static string FastPathShadowingParameter() => Shadow("abcd").ToString();

        public static string ObjectArgsSiblingCall() => HColumns.HUseColumn().ToString();

        public static string ConstantBeforeReader() => HLimits.HDerived.ToString();

        public static string UsingAliasInDeclaringFile() => HTagAlias.From(4).V.ToString();

        public static string LowercaseOwnerCaseTest()
        {
            HShape circle = new HShape.HCircle(2);
            HShape square = new HShape.HSquare(3);

            return $"{circle is HShape.HCircle} {square is HShape.HCircle}";
        }

        private static int Shadow(string hDoubleS) => HShadowed.HDouble(hDoubleS);
    }
}
