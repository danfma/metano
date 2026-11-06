namespace Metano.Tests;

/// <summary>
/// The module dispatcher of a generic overload group (#25): its body cannot
/// name the overloads' method type parameters, so delegating arguments cast
/// with their parameter type emitted <c>args[0] as T</c> — TS2304 at every
/// generic branch. The module bridge now lowers its arguments through the
/// same shared computation as the class dispatcher, which casts a generic
/// overload's arguments to <c>any</c> and lets the fast path infer its own.
/// The goldens pin the emitted shape — every branch casts to <c>any</c> and
/// none names a type parameter — and each golden file was checked by hand
/// under <c>tsgo --strict --noImplicitOverride</c> (scratch tsconfig); CI
/// compares the text only. Three shapes: a plain generic group, a generic
/// classic-extension group, and a group whose type parameter is constrained.
/// </summary>
public class ModuleDispatcherGenericGoldenTests
{
    [Test]
    public async Task GenericGroup_DelegatesArgumentsCastToAny()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Greets
            {
                public static T Pick<T>(T v) => v;

                public static T Pick<T>(T v, int n) => v;
            }

            [Transpile]
            public class Caller
            {
                public static string One() => Greets.Pick("a");

                public static string Two() => Greets.Pick("a", 2);
            }
            """
        );

        var outputs = string.Join(
            "\n",
            result["greets.ts"],
            result["caller.ts"],
            result["index.ts"]
        );

        await Assert.That(outputs).Contains("as any");

        await Assert.That(outputs).DoesNotContain("as T");

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("module-dispatch-generic-group.ts"));
    }

    [Test]
    public async Task GenericExtensionGroup_DelegatesArgumentsCastToAny()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Seqs
            {
                public static T First<T>(this T[] items) => items[0];

                public static T First<T>(this T[] items, T fallback) => fallback;
            }

            [Transpile]
            public class Caller
            {
                public static int Alone() => new[] { 1, 2 }.First();

                public static int WithFallback(int[] items) => items.First(9);
            }
            """
        );

        var outputs = string.Join("\n", result["seqs.ts"], result["caller.ts"], result["index.ts"]);

        await Assert.That(outputs).Contains("as any");

        await Assert.That(outputs).DoesNotContain("as T");

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("module-dispatch-generic-extension.ts"));
    }

    [Test]
    public async Task ConstrainedGroup_KeepsTheConstraintGuardAndCastsToAny()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            // No [Transpile] needed: the assembly attribute already transpiles
            // every public type of the compilation, `Ranked` included.
            public interface Ranked
            {
                int Rank { get; }
            }

            [NoContainer]
            public static class Ranks
            {
                public static T Max<T>(T a, T b) where T : Ranked => a;

                public static T Max<T>(T a, T b, T c) where T : Ranked => a;
            }

            [Transpile]
            public class Caller
            {
                public Ranked Top(Ranked a, Ranked b) => Ranks.Max(a, b);

                public Ranked TopThree(Ranked a, Ranked b, Ranked c) => Ranks.Max(a, b, c);
            }
            """
        );

        var outputs = string.Join(
            "\n",
            result["ranked.ts"],
            result["ranks.ts"],
            result["caller.ts"],
            result["index.ts"]
        );

        await Assert.That(outputs).Contains("as any");

        await Assert.That(outputs).DoesNotContain("as T");

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("module-dispatch-generic-constrained.ts"));
    }
}
