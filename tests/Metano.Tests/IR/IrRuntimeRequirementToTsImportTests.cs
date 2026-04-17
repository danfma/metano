using Metano.Compiler.IR;
using Metano.TypeScript.Bridge;

namespace Metano.Tests.IR;

public class IrRuntimeRequirementToTsImportTests
{
    [Test]
    public async Task EmptySet_ProducesNoImports()
    {
        var imports = IrRuntimeRequirementToTsImport.Convert(new HashSet<IrRuntimeRequirement>());
        await Assert.That(imports).IsEmpty();
    }

    [Test]
    public async Task HashCodeAndHashSet_AreMergedIntoSingleMetanoRuntimeImport()
    {
        var set = new HashSet<IrRuntimeRequirement>
        {
            new("HashCode", IrRuntimeCategory.Hashing),
            new("HashSet", IrRuntimeCategory.Collection),
        };
        var imports = IrRuntimeRequirementToTsImport.Convert(set);
        await Assert.That(imports).Count().IsEqualTo(1);
        await Assert.That(imports[0].Names).Contains("HashCode").And.Contains("HashSet");
        await Assert.That(imports[0].From).IsEqualTo("metano-runtime");
    }

    [Test]
    public async Task Temporal_LivesInTemporalPolyfillModule()
    {
        var set = new HashSet<IrRuntimeRequirement> { new("Temporal", IrRuntimeCategory.Temporal) };
        var imports = IrRuntimeRequirementToTsImport.Convert(set);
        await Assert.That(imports).Count().IsEqualTo(1);
        await Assert.That(imports[0].From).IsEqualTo("@js-temporal/polyfill");
    }

    [Test]
    public async Task Grouping_EmitsTypeOnlyImport()
    {
        var set = new HashSet<IrRuntimeRequirement>
        {
            new("Grouping", IrRuntimeCategory.Collection),
        };
        var imports = IrRuntimeRequirementToTsImport.Convert(set);
        await Assert.That(imports[0].TypeOnly).IsTrue();
    }

    [Test]
    public async Task UnknownHelper_IsIgnored()
    {
        var set = new HashSet<IrRuntimeRequirement>
        {
            new("SomethingWeird", IrRuntimeCategory.Equality),
        };
        var imports = IrRuntimeRequirementToTsImport.Convert(set);
        await Assert.That(imports).IsEmpty();
    }

    [Test]
    public async Task MultipleModules_AreGroupedDistinctly()
    {
        var set = new HashSet<IrRuntimeRequirement>
        {
            new("HashCode", IrRuntimeCategory.Hashing),
            new("Temporal", IrRuntimeCategory.Temporal),
        };
        var imports = IrRuntimeRequirementToTsImport.Convert(set);
        await Assert.That(imports).Count().IsEqualTo(2);
        await Assert
            .That(imports.Select(i => i.From))
            .Contains("metano-runtime")
            .And.Contains("@js-temporal/polyfill");
    }
}
