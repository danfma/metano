namespace Metano.Tests;

/// <summary>
/// Pins the full-namespace output layout (spec 005 / ADR-0025): a type's path is
/// its complete kebab-cased C# namespace and never shifts when sibling types in
/// other namespaces are added or removed.
/// </summary>
public class OutputLayoutTests
{
    [Test]
    public async Task SingleType_EmitsAtFullNamespacePath_NeverCollapsedToRoot()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Models;

            [Transpile]
            public sealed record Widget(string Name);
            """
        );

        await Assert.That(result).ContainsKey("app/models/widget.ts");
        await Assert.That(result).DoesNotContainKey("widget.ts");
    }

    [Test]
    public async Task TypePath_IsStable_WhenSiblingTypeInAnotherNamespaceIsAdded()
    {
        var single = TranspileHelper.Transpile(
            """
            namespace App.Models;

            [Transpile]
            public sealed record Widget(string Name);
            """
        );

        var withSibling = TranspileHelper.Transpile(
            """
            namespace App.Models
            {
                [Transpile]
                public sealed record Widget(string Name);
            }

            namespace App.Services
            {
                [Transpile]
                public sealed record Gadget(int Id);
            }
            """
        );

        await Assert.That(single).ContainsKey("app/models/widget.ts");
        // The unrelated sibling in App.Services must not relocate Widget.
        await Assert.That(withSibling).ContainsKey("app/models/widget.ts");
        await Assert.That(withSibling).ContainsKey("app/services/gadget.ts");
    }

    [Test]
    public async Task LeafBarrel_EmittedPerNamespace_WithoutRootBarrel()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Models;

            [Transpile]
            public sealed record Widget(string Name);
            """
        );

        await Assert.That(result).ContainsKey("app/models/index.ts");
        await Assert.That(result).DoesNotContainKey("index.ts");
    }
}
