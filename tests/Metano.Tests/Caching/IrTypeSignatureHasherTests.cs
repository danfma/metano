using Metano.Compiler;
using Metano.Compiler.Caching;
using Metano.Tests.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests.Caching;

/// <summary>
/// Pin the per-type signature hash so the per-group skip path
/// (PR 3b / ADR-0023) has a stable invalidation key. Coverage:
/// equal sources hash equal, body edits change the hash even when
/// signatures stay the same, signature edits change the hash, and
/// attribute edits change the hash.
/// </summary>
public class IrTypeSignatureHasherTests
{
    [Test]
    public async Task IdenticalSource_ProducesIdenticalHash()
    {
        var hashA = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile]
            public sealed record Foo(int Value);
            """
        );
        var hashB = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile]
            public sealed record Foo(int Value);
            """
        );
        await Assert.That(hashA).IsEqualTo(hashB);
    }

    [Test]
    public async Task BodyEdit_ChangesHash_EvenWithoutSignatureChange()
    {
        var before = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile]
            public sealed class Foo
            {
                public int Compute(int x) => x + 1;
            }
            """
        );
        var after = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile]
            public sealed class Foo
            {
                public int Compute(int x) => x + 2;
            }
            """
        );
        await Assert.That(before).IsNotEqualTo(after);
    }

    [Test]
    public async Task SignatureEdit_ChangesHash()
    {
        var before = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile]
            public sealed class Foo
            {
                public int Compute(int x) => x + 1;
            }
            """
        );
        var after = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile]
            public sealed class Foo
            {
                public int Compute(int x, int y) => x + y;
            }
            """
        );
        await Assert.That(before).IsNotEqualTo(after);
    }

    [Test]
    public async Task AttributeEdit_ChangesHash()
    {
        var before = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile]
            public sealed class Foo
            {
                public int Value { get; init; }
            }
            """
        );
        var after = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile, Name("Bar")]
            public sealed class Foo
            {
                public int Value { get; init; }
            }
            """
        );
        await Assert.That(before).IsNotEqualTo(after);
    }

    [Test]
    public async Task AttributeNamedArgEdit_ChangesHash()
    {
        var before = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile, Import("Foo", "foo", Version = "1.0.0")]
            public sealed class Foo {}
            """
        );
        var after = HashFirstTranspilableType(
            """
            namespace Demo;

            [Transpile, Import("Foo", "foo", Version = "2.0.0")]
            public sealed class Foo {}
            """
        );
        await Assert.That(before).IsNotEqualTo(after);
    }

    [Test]
    public async Task BaseTypeChange_ChangesHash()
    {
        var before = HashFirstTranspilableType(
            """
            namespace Demo;

            public abstract class BaseA {}

            [Transpile]
            public sealed class Foo : BaseA {}
            """,
            typeName: "Foo"
        );
        var after = HashFirstTranspilableType(
            """
            namespace Demo;

            public abstract class BaseB {}

            [Transpile]
            public sealed class Foo : BaseB {}
            """,
            typeName: "Foo"
        );
        await Assert.That(before).IsNotEqualTo(after);
    }

    private static string HashFirstTranspilableType(string source, string? typeName = null)
    {
        var compilation = IrTestHelper.Compile(source);
        var symbol = FindType(compilation, typeName);
        return IrTypeSignatureHasher.Hash(symbol);
    }

    private static INamedTypeSymbol FindType(CSharpCompilation compilation, string? typeName)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (model.GetDeclaredSymbol(node) is INamedTypeSymbol named && visited.Add(named))
                {
                    if (typeName is null || named.Name == typeName)
                        return named;
                }
            }
        }
        throw new InvalidOperationException($"Type '{typeName ?? "<first>"}' not found.");
    }
}
