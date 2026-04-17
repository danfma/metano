using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Tests.IR;

public class IrRuntimeRequirementScannerTests
{
    [Test]
    public async Task ClassWithGuidProperty_RequiresUuidBrandedType()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class User { public Guid Id { get; set; } }
            """
        );

        var requirements = IrRuntimeRequirementScanner.Scan(ir);
        await Assert.That(Contains(requirements, "UUID")).IsTrue();
    }

    [Test]
    public async Task ClassWithDateTime_RequiresTemporal()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Event { public DateTime Created { get; set; } }
            """
        );

        var requirements = IrRuntimeRequirementScanner.Scan(ir);
        await Assert.That(Contains(requirements, "Temporal")).IsTrue();
    }

    [Test]
    public async Task ClassWithHashSet_RequiresHashSetCollection()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Group { public HashSet<string> Names { get; } = new(); }
            """
        );

        var requirements = IrRuntimeRequirementScanner.Scan(ir);
        await Assert.That(Contains(requirements, "HashSet")).IsTrue();
    }

    [Test]
    public async Task Record_RequiresHashCode()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public record User(string Name);
            """
        );

        var requirements = IrRuntimeRequirementScanner.Scan(ir);
        await Assert.That(Contains(requirements, "HashCode")).IsTrue();
    }

    [Test]
    public async Task ClassWithPlainStrings_HasNoRuntimeRequirements()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Point { public int X; public int Y; }
            """
        );

        var requirements = IrRuntimeRequirementScanner.Scan(ir);
        await Assert.That(requirements).IsEmpty();
    }

    [Test]
    public async Task NullableGuid_StillReportsUuidRequirement()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class User { public Guid? Id { get; set; } }
            """
        );

        var requirements = IrRuntimeRequirementScanner.Scan(ir);
        await Assert.That(Contains(requirements, "UUID")).IsTrue();
    }

    // -- helpers --

    private static bool Contains(IReadOnlySet<IrRuntimeRequirement> set, string helperName) =>
        set.Any(r => r.HelperName == helperName);

    private static IrClassDeclaration ExtractClass(string csharpSource)
    {
        var compilation = IrTestHelper.Compile(csharpSource);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (
                    model.GetDeclaredSymbol(node) is INamedTypeSymbol named
                    && named.TypeKind is TypeKind.Class
                    && Metano.Compiler.SymbolHelper.HasTranspile(named)
                )
                {
                    return IrClassExtractor.Extract(named);
                }
            }
        }
        throw new InvalidOperationException("No [Transpile]-annotated class found.");
    }
}
