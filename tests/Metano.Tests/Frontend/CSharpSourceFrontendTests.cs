using Metano.Compiler;
using Metano.Tests.IR;

namespace Metano.Tests.Frontend;

/// <summary>
/// Producer-side tests for the bits of <see cref="IrCompilation"/> that
/// <see cref="CSharpSourceFrontend"/> already populates. Consumers (the
/// language targets) still build their own copy of this data; these
/// tests exist so the frontend cannot silently regress while the
/// migration is in flight.
/// </summary>
public class CSharpSourceFrontendTests
{
    [Test]
    public async Task BclExports_AreCollectedFromCurrentAssembly()
    {
        var compilation = IrTestHelper.Compile(
            """
            [assembly: ExportFromBcl(
                typeof(decimal),
                FromPackage = "decimal.js",
                ExportedName = "Decimal",
                Version = "^10.6.0"
            )]
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.BclExports).ContainsKey("decimal");
        var entry = ir.BclExports["decimal"];
        await Assert.That(entry.ExportedName).IsEqualTo("Decimal");
        await Assert.That(entry.FromPackage).IsEqualTo("decimal.js");
        await Assert.That(entry.Version).IsEqualTo("^10.6.0");
    }

    [Test]
    public async Task BclExports_OmitVersionWhenAttributeLeavesItEmpty()
    {
        var compilation = IrTestHelper.Compile(
            """
            [assembly: ExportFromBcl(
                typeof(decimal),
                FromPackage = "decimal.js",
                ExportedName = "Decimal"
            )]
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        var entry = ir.BclExports["decimal"];
        await Assert.That(entry.Version).IsNull();
    }

    [Test]
    public async Task BclExports_PullInDeclarationsFromReferencedAssemblies()
    {
        // Metano.Runtime declares [assembly: ExportFromBcl(typeof(decimal), ...)] for
        // decimal.js. The reference is brought in by IrTestHelper.BaseReferences via
        // TranspileHelper, so a project that doesn't redeclare the mapping should
        // still see it.
        var compilation = IrTestHelper.Compile(
            """
            [Transpile]
            public class Marker {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.BclExports).ContainsKey("decimal");
    }

    [Test]
    public async Task BclExports_IgnoreAttributesWithoutExportedName()
    {
        var compilation = IrTestHelper.Compile(
            """
            [assembly: ExportFromBcl(typeof(System.Guid), FromPackage = "uuid")]
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        // Resolve the same key the production code uses (typeArg.ToDisplayString()) so a
        // future format change can't accidentally make the assertion pass on the wrong key.
        var guidType = compilation.GetTypeByMetadataName("System.Guid");
        await Assert.That(guidType).IsNotNull();
        await Assert.That(ir.BclExports.ContainsKey(guidType!.ToDisplayString())).IsFalse();

        // Defence-in-depth: nothing else in the map should claim the "uuid" package
        // either, since the attribute lacked the required ExportedName property.
        foreach (var entry in ir.BclExports.Values)
            await Assert.That(entry.FromPackage).IsNotEqualTo("uuid");
    }

    [Test]
    public async Task ExternalImports_CollectImportAttributesFromCurrentAssembly()
    {
        var compilation = IrTestHelper.Compile(
            """
            [Import("Hono", from: "hono", Version = "^4.0.0")]
            public class Hono {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.ExternalImports).ContainsKey("Hono");
        var entry = ir.ExternalImports["Hono"];
        await Assert.That(entry.Name).IsEqualTo("Hono");
        await Assert.That(entry.From).IsEqualTo("hono");
        await Assert.That(entry.IsDefault).IsFalse();
        await Assert.That(entry.Version).IsEqualTo("^4.0.0");
    }

    [Test]
    public async Task ExternalImports_PreserveAsDefaultFlag()
    {
        var compilation = IrTestHelper.Compile(
            """
            [Import("React", from: "react", AsDefault = true)]
            public class React {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        var entry = ir.ExternalImports["React"];
        await Assert.That(entry.IsDefault).IsTrue();
        await Assert.That(entry.Version).IsNull();
    }

    [Test]
    public async Task ExternalImports_IgnoreTypesWithoutImportAttribute()
    {
        var compilation = IrTestHelper.Compile(
            """
            [Transpile]
            public class Plain {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.ExternalImports.ContainsKey("Plain")).IsFalse();
    }
}
