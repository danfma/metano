namespace Conformance.LazyThreeLevelCycle
{
    public class LzGapShape
    {
        public static LzGapShape Default;

        static LzGapShape()
        {
            Default = new LzGapCircle();
            Default = new LzGapTiny();
        }

        public virtual string Name => "shape";
    }

    public class LzGapCircle : LzGapShape
    {
        public override string Name => "circle";
    }

    public class LzGapTiny : LzGapCircle
    {
        public override string Name => "tiny";
    }

    public static class LazyThreeLevelCycleProbes
    {
        public static string MostDerivedFirst() => LzGapShape.Default.Name;
    }
}

namespace Conformance.LazyEagerSingletonCycle
{
    public class LzGapAnimal
    {
        public static LzGapAnimal Favorite;

        static LzGapAnimal()
        {
            Favorite = LzGapDog.Rex;
        }

        public virtual string Name => "animal";
    }

    public class LzGapDog : LzGapAnimal
    {
        public static readonly LzGapDog Rex = new LzGapDog();

        public override string Name => "rex";
    }

    public static class LazyEagerSingletonCycleProbes
    {
        public static string BaseFirst() =>
            LzGapAnimal.Favorite.Name + ":" + ReferenceEquals(LzGapAnimal.Favorite, LzGapDog.Rex);
    }
}

namespace Conformance.LazyEagerSingletonDerivedFirst
{
    public class DerivedFirstAnimal
    {
        public static DerivedFirstAnimal? Favorite;

        static DerivedFirstAnimal()
        {
            Favorite = DerivedFirstDog.Rex;
        }
    }

    public class DerivedFirstDog : DerivedFirstAnimal
    {
        public static DerivedFirstDog Rex { get; } = new DerivedFirstDog();
    }

    public static class LazyEagerSingletonDerivedFirstProbes
    {
        public static string DerivedFirst()
        {
            var rex = DerivedFirstDog.Rex;
            return (rex is not null) + ":" + (DerivedFirstAnimal.Favorite is null);
        }
    }
}
