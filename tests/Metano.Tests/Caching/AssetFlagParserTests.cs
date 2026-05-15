using Metano.Compiler;

namespace Metano.Tests.Caching;

/// <summary>
/// Pins the CLI <c>--asset</c> parsing contract. Both transpiler CLIs
/// (TypeScript / Dart) route their <c>string[]?</c> flag through
/// <see cref="AssetFlagParser"/> so the host receives uniform
/// <see cref="AssetSpec"/> instances regardless of which target ran.
/// </summary>
public class AssetFlagParserTests
{
    [Test]
    public async Task Parse_ReturnsEmpty_OnNull()
    {
        var assets = AssetFlagParser.Parse(null);
        await Assert.That(assets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_BareSource_OmitsRelativeDestination()
    {
        var assets = AssetFlagParser.Parse(["/abs/schema.sql"]);

        await Assert.That(assets.Count).IsEqualTo(1);
        await Assert.That(assets[0].Source).IsEqualTo("/abs/schema.sql");
        await Assert.That(assets[0].RelativeDestination).IsNull();
    }

    [Test]
    public async Task Parse_SourceEqualsDestination_SplitsOnFirstEquals()
    {
        var assets = AssetFlagParser.Parse(["/abs/schema.sql=provider/schema.sql"]);

        await Assert.That(assets[0].Source).IsEqualTo("/abs/schema.sql");
        await Assert.That(assets[0].RelativeDestination).IsEqualTo("provider/schema.sql");
    }

    [Test]
    public async Task Parse_TrailingEqualsSign_ProducesNullDestination()
    {
        var assets = AssetFlagParser.Parse(["/abs/schema.sql="]);

        await Assert.That(assets[0].Source).IsEqualTo("/abs/schema.sql");
        await Assert.That(assets[0].RelativeDestination).IsNull();
    }

    [Test]
    public async Task Parse_SkipsBlankEntries()
    {
        var assets = AssetFlagParser.Parse(["", "   ", "/abs/schema.sql"]);

        await Assert.That(assets.Count).IsEqualTo(1);
        await Assert.That(assets[0].Source).IsEqualTo("/abs/schema.sql");
    }

    [Test]
    public async Task Parse_MultipleEquals_SplitsOnFirstOnly()
    {
        var assets = AssetFlagParser.Parse(["/abs/foo=a=b"]);

        await Assert.That(assets[0].Source).IsEqualTo("/abs/foo");
        await Assert.That(assets[0].RelativeDestination).IsEqualTo("a=b");
    }
}
