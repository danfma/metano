namespace Conformance
{
    /// <summary>
    /// #743: <c>Twig&lt;T&gt;</c> and <c>Conformance.Arity.Twig</c> print under one name;
    /// each module imports the one it names, not the one registered last.
    /// </summary>
    public static class SameNameImportProbes
    {
        public static string GenericTwigImportsItsOwnModule()
        {
            Twig<int> twig = new TwigLeaf<int>(4);

            return (twig is TwigLeaf<int> leaf ? leaf.Value : -1) + "|" + (twig is Twig<int>);
        }

        public static string NonGenericTwigImportsItsOwnModule() =>
            Arity.TwigSizes.Of(new Arity.Bush());
    }

    public abstract record Twig<T>;

    public sealed record TwigLeaf<T>(T Value) : Twig<T>;
}

namespace Conformance.Arity
{
    public abstract class Twig
    {
        public abstract int Size();
    }

    public sealed class Bush : Twig
    {
        public override int Size() => 3;
    }

    public static class TwigSizes
    {
        public static string Of(Twig twig) => "size:" + twig.Size();
    }
}
