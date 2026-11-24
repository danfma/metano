using Metano.Annotations;

namespace Conformance.Specifiers.Deep.Inner.Most
{
    public class DeepThing
    {
        public static string Describe() =>
            "deep<" + Conformance.Specifiers.Shallow.ShallowThing.Tag + ">";

        public string Greet() => new Conformance.SpecifierRoot().Hello() + "/deep";
    }
}

namespace Conformance.Specifiers.Shallow
{
    public class ShallowThing
    {
        public static readonly string Tag =
            "shallow-" + Conformance.Specifiers.Sibling.SiblingSeed.Value;
    }
}

namespace Conformance.Specifiers.Sibling
{
    public static class SiblingSeed
    {
        public static readonly int Value = 6 * 7;
    }

    [EmitInFile("specifier-pair")]
    public class PairLeft
    {
        public int V = 1;
    }

    [EmitInFile("specifier-pair")]
    public class PairRight
    {
        public int V = 2;
    }

    public interface ISpecifierShape
    {
        int Area();
    }

    public static class ShapeMeasure
    {
        public static string Measure(ISpecifierShape shape) => "area=" + shape.Area();
    }
}

namespace Conformance.Specifiers.Other
{
    public class SpecifierSquare : Conformance.Specifiers.Sibling.ISpecifierShape
    {
        public int Side = 3;

        public int Area() => Side * Side;
    }
}

namespace Conformance
{
    public class SpecifierRoot
    {
        public string Hello() => "root";

        public static string Down() => Conformance.Specifiers.Deep.Inner.Most.DeepThing.Describe();
    }

    public static class ModuleSpecifierProbes
    {
        public static string DeepToShallow() => Specifiers.Deep.Inner.Most.DeepThing.Describe();

        public static string NestedToRoot() => new Specifiers.Deep.Inner.Most.DeepThing().Greet();

        public static string RootToNested() => SpecifierRoot.Down();

        public static string CoLocatedTarget() =>
            (new Specifiers.Sibling.PairLeft().V + new Specifiers.Sibling.PairRight().V).ToString();

        public static string StaticInitializerAcrossNamespaces() =>
            Specifiers.Shallow.ShallowThing.Tag;

        public static string TypeOnlyImportAcrossSiblings() =>
            Specifiers.Sibling.ShapeMeasure.Measure(new Specifiers.Other.SpecifierSquare());
    }
}
