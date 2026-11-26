namespace Metano.Tests;

/// <summary>
/// An internal class derived from a public class owns its runtime type descriptor and
/// its interface members stay public API (#819).
/// </summary>
public class InternalRuntimeTypeTests
{
    private const string Source = """
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("probe-pkg")]

        public abstract class OwnerNode
        {
            internal void Dispose() { }
        }

        internal abstract class EffectNode : OwnerNode, IDisposable
        {
            void IDisposable.Dispose() => Dispose();
        }

        internal sealed class SplitEffect<T> : EffectNode { }

        public interface IWork { void Run(); int Size { get; } }
        public interface IWork2 { void Go(); }
        internal sealed class Job : IWork
        {
            void IWork.Run() { }
            int IWork.Size => 1;
        }
        internal class Job2 : IWork2
        {
            public void Go() { }
        }
        public static class Reactivity
        {
            public static IDisposable Effect<T>() => new SplitEffect<T>();
            public static IWork Make() => new Job();
            public static IWork2 Make2() => new Job2();
        }
        """;

    [Test]
    public async Task InternalDescendantOfAPublicClass_OwnsItsDescriptorWithItsInterfaces()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["split-effect.ts"])
            .Contains("static override get $runtimeType(): ClassType<SplitEffect<unknown>>")
            .And.Contains("base: () => EffectNode.$runtimeType,")
            .And.Contains("() => IDisposable$runtimeType,")
            .And.Contains("typeParameters: [\"T\"],");
        await Assert
            .That(result["effect-node.ts"])
            .Contains("static override get $runtimeType(): ClassType<EffectNode>")
            .And.Contains("() => IDisposable$runtimeType,");
        await Assert.That(result["job.ts"]).Contains("() => IWork$runtimeType,");
    }

    [Test]
    public async Task InternalTypes_StayOutOfTheBarrel()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["index.ts"])
            .DoesNotContain("EffectNode")
            .And.DoesNotContain("SplitEffect")
            .And.DoesNotContain("Job");
    }

    [Test]
    public async Task ClaimingMemberOfADroppedForwarder_IsNotMarkedInternal()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert.That(result["owner-node.ts"]).DoesNotContain("@internal");
        await Assert.That(result["owner-node.ts"]).Contains("  dispose(): void {");
    }

    [Test]
    public async Task InternalMemberNoPublicContractRelies_StaysInternal()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Plain
            {
                internal void Dispose() { }
                internal void Other() { }
            }

            public class Owner : Plain, IDisposable
            {
                void IDisposable.Dispose() => Dispose();
            }
            """
        );

        await Assert.That(result["plain.ts"]).Contains("  dispose(): void {");
        await Assert.That(result["plain.ts"]).Contains("/** @internal */\n  other(): void {");
        await Assert.That(result["plain.ts"]).DoesNotContain("/** @internal */\n  dispose");
    }

    [Test]
    public async Task InternalClassImplementingAUserInterface_KeepsItsMembersPublic()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert.That(result["job.ts"]).DoesNotContain("@internal");
        await Assert.That(result["job2.ts"]).DoesNotContain("@internal");
        await Assert.That(result["job2.ts"]).Contains("  go(): void {");
    }
}
