namespace Metano.Tests;

/// <summary>
/// Reproduces the reported Vigiata.Contracts scenario end to end (SC-007): a
/// namespace-declaring contracts library emitted under an import alias lands at
/// stable full-namespace paths, exposes no stale root barrel or root type file,
/// and references types across namespaces by direct full-namespace file path.
/// </summary>
public class DeterministicLayoutScenarioTests
{
    private const string ContractsSource = """
        namespace Vigiata.Contracts.Profiles
        {
            [Name("UserProfile")]
            [Transpile]
            public sealed record UserProfileDto(string Id);
        }

        namespace Vigiata.Contracts.Serialization
        {
            [Transpile]
            public sealed record ContractsSerializerContext(
                Vigiata.Contracts.Profiles.UserProfileDto Profile
            );
        }
        """;

    [Test]
    public async Task ContractsLibrary_EmitsStableFullNamespaceLayout_NoStaleRoot()
    {
        var result = TranspileHelper.Transpile(
            ContractsSource,
            importAlias: "web-server-contracts"
        );

        await Assert.That(result).ContainsKey("vigiata/contracts/profiles/user-profile.ts");
        await Assert
            .That(result)
            .ContainsKey("vigiata/contracts/serialization/contracts-serializer-context.ts");

        // The reported defect was a stale root barrel / root type file that masked a
        // broken `../contracts` import. Neither may exist in clean generation.
        await Assert.That(result).DoesNotContainKey("user-profile.ts");
        await Assert.That(result).DoesNotContainKey("index.ts");
    }

    [Test]
    public async Task CrossNamespaceReference_UsesFullNamespaceAliasFile()
    {
        var result = TranspileHelper.Transpile(
            ContractsSource,
            importAlias: "web-server-contracts"
        );

        var serializer = result["vigiata/contracts/serialization/contracts-serializer-context.ts"];
        await Assert
            .That(serializer)
            .Contains("from \"#web-server-contracts/vigiata/contracts/profiles/user-profile\"");
    }
}
