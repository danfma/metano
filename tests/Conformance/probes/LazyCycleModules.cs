using Metano.Annotations;

namespace Conformance.LazyBarrel
{
    // A base whose static constructor constructs a derived class from another file
    // (spec 001 FR-066, #681). The two modules import each other, so they load only
    // when the derived module is evaluated first: the generated barrel lists it first.

    public class LzShape
    {
        static LzShape()
        {
            Default = new LzSquare();
        }

        public static LzShape Default { get; }

        public virtual string Name => "shape";
    }

    public class LzSquare : LzShape
    {
        public override string Name => "square";
    }

    // Run with the barrel (index) loaded first: tools/preloads.txt.
    public static class LazyBarrelProbes
    {
        public static string ViaBarrel() => LzShape.Default.Name;
    }

    // The same cycle imported straight from the base module: the derived class's
    // `extends` clause reads the base before it exists (the documented gap).
    public static class LazyDirectProbes
    {
        public static string BaseModuleFirst() => LzShape.Default.Name;
    }
}

namespace Conformance
{
    // A cycle whose derived module sorts first, so the probe module imports it first.
    public class LzPolygon
    {
        static LzPolygon()
        {
            Default = new LzHexagon();
        }

        public static LzPolygon Default { get; }

        public virtual string Name => "polygon";
    }

    public class LzHexagon : LzPolygon
    {
        public override string Name => "hexagon";
    }

    public static class LazyDerivedFirstProbes
    {
        public static string DerivedModuleFirst() =>
            new LzHexagon().Name + "|" + LzPolygon.Default.Name;
    }

    // Co-located in one file with the derived class declared first: the file prints
    // the base first.
    [EmitInFile("lazy-pair")]
    public class LzPairDerived : LzPairBase
    {
        public override string Name => "derived";
    }

    [EmitInFile("lazy-pair")]
    public class LzPairBase
    {
        static LzPairBase()
        {
            Default = new LzPairDerived();
        }

        public static LzPairBase Default { get; }

        public virtual string Name => "base";
    }

    public static class LazyCoLocatedProbes
    {
        public static string DerivedDeclaredFirst() => LzPairBase.Default.Name;
    }
}
