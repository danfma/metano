using Metano.Compiler;
using Metano.Compiler.Analysis;
using Metano.Compiler.Caching;
using Metano.Tests.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests.Caching;

/// <summary>
/// Pin the closure-hash semantics for PR 3b's per-group skip:
/// editing a type in the closure flips the hash, editing a type
/// outside the closure does NOT flip it.
/// </summary>
public class GroupClosureHasherTests
{
    [Test]
    public async Task SameClosure_AcrossRuns_ProducesSameHash()
    {
        var a = HashClosureOf("Demo.Order", BaselineSource);
        var b = HashClosureOf("Demo.Order", BaselineSource);
        await Assert.That(a).IsEqualTo(b);
    }

    [Test]
    public async Task EditingSeedType_FlipsHash()
    {
        var before = HashClosureOf("Demo.Order", BaselineSource);
        var after = HashClosureOf(
            "Demo.Order",
            BaselineSource.Replace("record Order(Money Total)", "record Order(Money Total, int Quantity)")
        );
        await Assert.That(before).IsNotEqualTo(after);
    }

    [Test]
    public async Task EditingDependency_FlipsHash()
    {
        var before = HashClosureOf("Demo.Order", BaselineSource);
        // Edit Money (Order's dependency) — Order's closure includes Money,
        // so the hash must flip even though Order itself didn't change.
        var after = HashClosureOf(
            "Demo.Order",
            BaselineSource.Replace("record Money(decimal Amount, string Currency)", "record Money(decimal Amount, string Currency, string Note)")
        );
        await Assert.That(before).IsNotEqualTo(after);
    }

    [Test]
    public async Task EditingTypeOutsideClosure_KeepsHash()
    {
        var before = HashClosureOf("Demo.Order", BaselineSource);
        // Customer is transpilable but NOT in Order's closure (Order does
        // not reference Customer). Editing Customer must not invalidate
        // Order's group.
        var after = HashClosureOf(
            "Demo.Order",
            BaselineSource.Replace("record Customer(string Name)", "record Customer(string Name, string Email)")
        );
        await Assert.That(before).IsEqualTo(after);
    }

    private const string BaselineSource =
        """
        namespace Demo;

        [Transpile]
        public sealed record Money(decimal Amount, string Currency);

        [Transpile]
        public sealed record Order(Money Total);

        [Transpile]
        public sealed record Customer(string Name);
        """;

    private static string HashClosureOf(string fqn, string source)
    {
        var compilation = IrTestHelper.Compile(source);
        var transpilable = EnumerateTranspilableTypes(compilation).ToList();
        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);
        var graph = IrTypeDependencyGraph.Build(ir);
        var sigIndex = GroupClosureHasher.BuildSignatureHashIndex(transpilable);
        return GroupClosureHasher.HashGroupClosure(new[] { fqn }, graph, sigIndex);
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTranspilableTypes(
        CSharpCompilation compilation
    )
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (
                    model.GetDeclaredSymbol(node) is INamedTypeSymbol named
                    && named.GetAttributes().Any(a => a.AttributeClass?.Name == "TranspileAttribute")
                )
                    yield return named;
            }
        }
    }
}
