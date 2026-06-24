namespace Metano.Tests;

/// <summary>
/// Pins the internal import contract (D3 / FR-016): references between generated
/// types resolve to the defining file directly, never through a namespace barrel —
/// keeping internal correctness independent of barrel generation and structurally
/// preventing ESM import cycles.
/// </summary>
public class ImportContractTests
{
    [Test]
    public async Task CrossNamespaceReference_ImportsTheFileDirectly_NotTheBarrel()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace Demo.Models
            {
                [Transpile]
                public sealed record Address(string City);
            }

            namespace Demo.Users
            {
                [Transpile]
                public sealed record Person(string Name, Demo.Models.Address Home);
            }
            """
        );

        var person = result["demo/users/person.ts"];
        // Paired assertion: the import must target the file, and must NOT degrade back
        // to the namespace barrel (`#/demo/models`) — keep both so neither loosens.
        await Assert.That(person).Contains("from \"#/demo/models/address\"");
        await Assert.That(person).DoesNotContain("from \"#/demo/models\";");
    }

    [Test]
    public async Task SameNamespaceReference_StaysRelativeToTheFile()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace Demo.Models;

            [Transpile]
            public sealed record Address(string City);

            [Transpile]
            public sealed record Company(string Name, Address Hq);
            """
        );

        var company = result["demo/models/company.ts"];
        await Assert.That(company).Contains("from \"./address\"");
    }
}
