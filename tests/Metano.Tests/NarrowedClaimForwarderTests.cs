namespace Metano.Tests;

/// <summary>
/// #821: a pure forwarder of a public interface member to a less accessible inherited
/// member is published on the implementing class when the shapes line up, and the claiming
/// member keeps its tag. Every shape is type-checked with <c>tsgo --strict</c>
/// (<c>noImplicitOverride</c>).
/// </summary>
public class NarrowedClaimForwarderTests
{
    private static async Task<Dictionary<string, string>> StrictFiles(string source)
    {
        var files = TranspileHelper.Transpile(source);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);

        return files;
    }

    [Test]
    public async Task ReturnTypeDiffersFromTheClaimer_KeepsTheClaimerPublicAndDropsTheForwarder()
    {
        var files = await StrictFiles(
            """
            [assembly: TranspileAssembly]

            public class Base { internal Base Clone() => this; }
            public sealed class Derived : Base, ICloneable
            {
                object ICloneable.Clone() => Clone();
            }
            """
        );

        await Assert.That(files["base.ts"]).DoesNotContain("@internal");
        await Assert.That(files["base.ts"]).Contains("  clone(): Base {");
        await Assert.That(files["derived.ts"]).DoesNotContain("clone()");
    }

    [Test]
    public async Task BlockBodiedValueReturningForwarder_IsRebound()
    {
        var files = await StrictFiles(
            """
            [assembly: TranspileAssembly]

            public abstract class Base { internal int CompareTo(object? other) => 0; }
            public sealed class Derived : Base, IComparable
            {
                int IComparable.CompareTo(object? other)
                {
                    return CompareTo(other);
                }
            }
            """
        );

        await Assert.That(files["base.ts"]).Contains("/** @internal */\n  compareTo(");

        await Assert
            .That(files["derived.ts"])
            .Contains("override compareTo(other: Object | null): number {")
            .And.Contains("return super.compareTo(other);");
    }

    [Test]
    public async Task GenericBaseClaimer_IsReboundThroughTheConstructedBase()
    {
        var files = await StrictFiles(
            """
            [assembly: TranspileAssembly]

            public abstract class Base<T> { internal int CompareTo(T other) => 0; }
            public sealed class Derived : Base<string>, IComparable<string>
            {
                int IComparable<string>.CompareTo(string? other) => CompareTo(other!);
            }
            """
        );

        await Assert.That(files["base.ts"]).Contains("/** @internal */\n  compareTo(");
        await Assert.That(files["derived.ts"]).Contains("super.compareTo(other");
    }

    [Test]
    public async Task VirtualClaimerOverriddenInAnIntermediateClass_ForwardsToTheNearestOne()
    {
        var files = await StrictFiles(
            """
            [assembly: TranspileAssembly]

            public abstract class Base { internal virtual void Dispose() { } }
            public abstract class Mid : Base { internal override void Dispose() { } }
            public sealed class Effect : Mid, IDisposable
            {
                void IDisposable.Dispose() => Dispose();
            }
            """
        );

        await Assert.That(files["base.ts"]).Contains("/** @internal */\n  dispose(): void {");

        await Assert
            .That(files["mid.ts"])
            .Contains("/** @internal */\n  override dispose(): void {");

        await Assert.That(files["effect.ts"]).Contains("override dispose(): void {");
        await Assert.That(files["effect.ts"]).Contains("super.dispose();");
    }

    [Test]
    public async Task TwoForwardersOfDifferentInterfaces_ClaimEachOther_AndStayDropped()
    {
        // Each explicit member finds the other one first as the member that claims the
        // name (FindClaimingMember scans the class before its bases), so neither forwards
        // to the inherited claimer: both are dropped as before and the claimer keeps its tag.
        var files = await StrictFiles(
            """
            [assembly: TranspileAssembly]

            public abstract class Base { internal int CompareTo(object? other) => 0; }
            public sealed class Derived : Base, IComparable, IComparable<Derived>
            {
                int IComparable.CompareTo(object? other) => CompareTo(other);
                int IComparable<Derived>.CompareTo(Derived? other) => CompareTo(other);
            }
            """
        );

        await Assert.That(files["base.ts"]).Contains("/** @internal */\n  compareTo(");
        await Assert.That(files["derived.ts"]).DoesNotContain("compareTo");
    }

    [Test]
    public async Task GenericMethodForwarder_IsNotPublished_AsBefore()
    {
        // `T` of the explicit member and `T` of the claimer are different type parameters,
        // so the return types are not identical and the forwarder is not published. The
        // output is the one of 4.0.0 (the claimer stays tagged, a known gap).
        var files = TranspileHelper.TranspileWithLibrary(
            """
            public interface IMaker { T Make<T>(); }
            """,
            """
            [assembly: TranspileAssembly]

            public abstract class Base { internal T Make<T>() => default!; }
            public sealed class Impl : Base, IMaker
            {
                T IMaker.Make<T>() => Make<T>();
            }
            """
        );

        await Assert.That(files["impl.ts"]).DoesNotContain("make");
        await Assert.That(files["base.ts"]).Contains("/** @internal */\n  make<T>(): T {");
    }

    [Test]
    public async Task DartKeepsTheClaimerWidenedAndTheForwarderDropped()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using System;
            using Metano.Annotations;

            [Transpile]
            public abstract class OwnerNode { internal void Dispose() { } }

            [Transpile]
            public sealed class EffectNode : OwnerNode, IDisposable
            {
                void IDisposable.Dispose() => Dispose();
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "narrowed-claim-forwarder.dart",
            string.Join("\n// ----\n", files.OrderBy(file => file.Key).Select(file => file.Value))
        );
    }
}
