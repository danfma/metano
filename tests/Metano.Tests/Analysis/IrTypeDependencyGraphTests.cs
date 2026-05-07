using Metano.Compiler;
using Metano.Compiler.Analysis;
using Metano.Tests.IR;

namespace Metano.Tests.DependencyGraph;

/// <summary>
/// Behavior coverage for <see cref="IrTypeDependencyGraph"/>. The
/// graph is the shared backbone for incremental compilation (#21) and
/// watch mode (#18); the tests pin its forward + reverse edges so the
/// downstream cache and file watcher have a stable contract.
/// </summary>
public class IrTypeDependencyGraphTests
{
    [Test]
    public async Task DirectFieldReference_AddsForwardAndReverseEdge()
    {
        var graph = BuildGraph(
            """
            namespace Demo;

            [Transpile]
            public sealed record Money(decimal Amount, string Currency);

            [Transpile]
            public sealed record Order(Money Total);
            """
        );

        await Assert.That(graph.DependenciesOf("Demo.Order")).Contains("Demo.Money");
        await Assert.That(graph.DependentsOf("Demo.Money")).Contains("Demo.Order");
    }

    [Test]
    public async Task GenericTypeArgument_RegistersTheArgumentNotTheContainer()
    {
        // Generics like List<User> resolve to IrArrayTypeRef once the
        // collection-like lowering kicks in; the inner User reference
        // still needs to land in the graph so a change to User
        // invalidates the consumer.
        var graph = BuildGraph(
            """
            using System.Collections.Generic;

            namespace Demo;

            [Transpile]
            public sealed record User(string Name);

            [Transpile]
            public sealed record Roster(List<User> Members);
            """
        );

        await Assert.That(graph.DependenciesOf("Demo.Roster")).Contains("Demo.User");
        await Assert.That(graph.DependentsOf("Demo.User")).Contains("Demo.Roster");
    }

    [Test]
    public async Task TransitiveDependents_ChainAtoBtoC_ReachesA()
    {
        var graph = BuildGraph(
            """
            namespace Demo;

            [Transpile]
            public sealed record A(int X);

            [Transpile]
            public sealed record B(A Inner);

            [Transpile]
            public sealed record C(B Outer);
            """
        );

        var transitiveOfA = graph.TransitiveDependentsOf("Demo.A");
        await Assert.That(transitiveOfA).Contains("Demo.B");
        await Assert.That(transitiveOfA).Contains("Demo.C");
    }

    [Test]
    public async Task SelfReference_IsNotRecorded()
    {
        // A `with`-style fluent return type produces a self-reference
        // in the IR. Recording it would inflate the graph and make
        // every change to the type look like it had an extra
        // dependent that is just itself.
        var graph = BuildGraph(
            """
            namespace Demo;

            [Transpile]
            public sealed record Step(int Order)
            {
                public Step Next() => new(Order + 1);
            }
            """
        );

        await Assert.That(graph.DependenciesOf("Demo.Step")).DoesNotContain("Demo.Step");
    }

    [Test]
    public async Task TypeOutsideCompilation_IsDropped()
    {
        // BCL types (System.Decimal, System.String) and any other
        // assembly-foreign reference cannot be invalidated from this
        // compilation's cache, so the graph drops them. The cache
        // tracks foreign assemblies via their metadata hash on a
        // separate axis.
        var graph = BuildGraph(
            """
            using System;

            namespace Demo;

            [Transpile]
            public sealed record Stamp(DateTime When, string Note);
            """
        );

        var deps = graph.DependenciesOf("Demo.Stamp");
        await Assert.That(deps).DoesNotContain("System.DateTime");
        await Assert.That(deps).DoesNotContain("System.String");
    }

    [Test]
    public async Task NestedTypeReference_AttributesToTopLevelContainer()
    {
        // A reference to Outer.Inner attributes back to Outer because
        // the cache emits Outer's output as a whole — Inner cannot be
        // regenerated independently. Without the top-level rollup the
        // ownTypes lookup would miss and the edge would silently drop.
        var graph = BuildGraph(
            """
            namespace Demo;

            [Transpile]
            public sealed class Outer
            {
                public sealed record Inner(int X);
            }

            [Transpile]
            public sealed record Holder(Outer.Inner Slot);
            """
        );

        await Assert.That(graph.DependenciesOf("Demo.Holder")).Contains("Demo.Outer");
        await Assert.That(graph.DependentsOf("Demo.Outer")).Contains("Demo.Holder");
    }

    [Test]
    public async Task SameNameDifferentArity_KeepsKeysDistinct()
    {
        // Foo and Foo<T> are different types in C#; collapsing them
        // would corrupt outEdges (one would silently overwrite the
        // other) and break invalidation for projects that overload by
        // arity.
        var graph = BuildGraph(
            """
            namespace Demo;

            [Transpile]
            public sealed record Foo(int X);

            [Transpile]
            public sealed record Foo<T>(T Value);
            """
        );

        var allTypes = graph.AllTypes.ToHashSet();
        await Assert.That(allTypes).Contains("Demo.Foo");
        await Assert.That(allTypes).Contains("Demo.Foo<T>");
    }

    [Test]
    public async Task TransitiveDependents_OnCyclicGraph_ExcludesSeed()
    {
        // A → B → A is a cycle. The transitive walker re-adds the
        // seed via a neighbor while traversing; the closure helper
        // must strip it back out so the documented "excludes seed"
        // contract stays honest even with cycles in the graph.
        var graph = BuildGraph(
            """
            namespace Demo;

            [Transpile]
            public sealed class A
            {
                public B? Other { get; init; }
            }

            [Transpile]
            public sealed class B
            {
                public A? Other { get; init; }
            }
            """
        );

        var transitiveOfA = graph.TransitiveDependentsOf("Demo.A");
        await Assert.That(transitiveOfA).Contains("Demo.B");
        await Assert.That(transitiveOfA).DoesNotContain("Demo.A");
    }

    [Test]
    public async Task UnknownType_ReturnsEmptySets()
    {
        var graph = BuildGraph(
            """
            namespace Demo;

            [Transpile]
            public sealed record A(int X);
            """
        );

        await Assert.That(graph.DependenciesOf("Demo.Missing")).IsEmpty();
        await Assert.That(graph.DependentsOf("Demo.Missing")).IsEmpty();
        await Assert.That(graph.TransitiveDependentsOf("Demo.Missing")).IsEmpty();
    }

    private static IrTypeDependencyGraph BuildGraph(string csharpSource)
    {
        var compilation = IrTestHelper.Compile(csharpSource);
        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);
        return IrTypeDependencyGraph.Build(ir);
    }
}
