namespace Metano.Tests;

/// <summary>
/// A constructor's base call and its body share one scope of temporaries
/// (W13-1b, #804): the temporaries both assign inline are declared once, ahead
/// of <c>super(...)</c>, with distinct names — for a single constructor and for
/// each branch of an overloaded constructor's dispatcher.
/// </summary>
public class ConstructorTemporaryScopeTests
{
    [Test]
    public async Task BaseCallAndBody_ShareOneScopeOfTemporaries()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Box
            {
                public int V { get; set; }
            }

            [Transpile]
            public static class Order
            {
                public static int G(int v) => v;

                public static int F(int a, int b) => a * 10 + b;
            }

            [Transpile]
            public class Base
            {
                public Base(Box box) => Value = box.V;

                public Base(int value) => Value = value;

                public int Value { get; }
            }

            [Transpile]
            public class Single : Base
            {
                public Single(int p)
                    : base(new Box { V = p }) => Kept = new Box { V = p + 1 };

                public Box Kept { get; }
            }

            [Transpile]
            public class Reordered : Base
            {
                public Reordered()
                    : base(Order.F(b: Order.G(1), a: Order.G(2))) =>
                    Q = Order.F(b: Order.G(3), a: Order.G(4));

                public int Q { get; }
            }

            [Transpile]
            public class Overloaded : Base
            {
                public Overloaded(int p)
                    : base(new Box { V = p }) => Kept = new Box { V = p + 1 };

                public Overloaded(string s)
                    : base(Order.F(b: Order.G(1), a: s.Length)) =>
                    Kept = new Box { V = Order.F(b: Order.G(3), a: Order.G(4)) };

                public Box Kept { get; }
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "constructor-shared-temporaries.ts",
            string.Join("\n", result["single.ts"], result["reordered.ts"], result["overloaded.ts"])
        );
    }
}
