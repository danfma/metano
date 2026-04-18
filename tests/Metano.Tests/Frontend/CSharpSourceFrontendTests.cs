using Metano.Compiler;
using Metano.Compiler.Diagnostics;
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

    [Test]
    public async Task CrossAssemblyOrigins_RegisterTypesFromTranspilableLibrary()
    {
        var lib = TranspileHelper.CompileLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-shared")]

            namespace Acme.Shared.Domain
            {
                [Transpile]
                public class Money { public int Amount { get; } }
            }
            """,
            assemblyName: "AcmeShared"
        );

        var consumer = TranspileHelper.CompileConsumer(
            """
            using Acme.Shared.Domain;

            [Transpile]
            public class Marker { public Money? M { get; } }
            """,
            lib
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(consumer);

        var moneyType = consumer.GetTypeByMetadataName("Acme.Shared.Domain.Money");
        await Assert.That(moneyType).IsNotNull();
        var key = moneyType!.GetStableFullName();

        await Assert.That(ir.CrossAssemblyOrigins).ContainsKey(key);
        var origin = ir.CrossAssemblyOrigins[key];
        await Assert.That(origin.PackageId).IsEqualTo("acme-shared");
        await Assert.That(origin.Namespace).IsEqualTo("Acme.Shared.Domain");
        await Assert.That(origin.AssemblyRootNamespace).IsEqualTo("Acme.Shared.Domain");
    }

    [Test]
    public async Task AssembliesNeedingEmitPackage_RecordsTranspilableLibraryWithoutEmitPackage()
    {
        var lib = TranspileHelper.CompileLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Orphan
            {
                [Transpile]
                public class Detached {}
            }
            """,
            assemblyName: "OrphanLib"
        );

        var consumer = TranspileHelper.CompileConsumer(
            """
            [Transpile]
            public class Marker {}
            """,
            lib
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(consumer);

        await Assert.That(ir.AssembliesNeedingEmitPackage).Contains("OrphanLib");
        await Assert.That(ir.CrossAssemblyOrigins.Keys).DoesNotContain("Orphan.Detached");
    }

    [Test]
    public async Task CrossAssemblyOrigins_SkipsImportAndNoEmitTypes()
    {
        var lib = TranspileHelper.CompileLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-mixed")]

            namespace Acme.Mixed
            {
                [Transpile]
                public class Real {}

                [Import("Hono", from: "hono")]
                public class HonoStub {}

                [NoEmit]
                public class Ambient {}
            }
            """,
            assemblyName: "AcmeMixed"
        );

        var consumer = TranspileHelper.CompileConsumer(
            """
            [Transpile]
            public class Marker {}
            """,
            lib
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(consumer);

        var realKey = consumer.GetTypeByMetadataName("Acme.Mixed.Real")!.GetStableFullName();
        var honoKey = consumer.GetTypeByMetadataName("Acme.Mixed.HonoStub")!.GetStableFullName();
        var ambientKey = consumer.GetTypeByMetadataName("Acme.Mixed.Ambient")!.GetStableFullName();

        await Assert.That(ir.CrossAssemblyOrigins).ContainsKey(realKey);
        await Assert.That(ir.CrossAssemblyOrigins.ContainsKey(honoKey)).IsFalse();
        await Assert.That(ir.CrossAssemblyOrigins.ContainsKey(ambientKey)).IsFalse();
    }

    [Test]
    public async Task CrossAssemblyOrigins_RootNamespaceIgnoresNoEmitAndNoTranspileTypes()
    {
        // The reference declares emitted types under Acme.Mixed.* but also has
        // [NoEmit] / [NoTranspile] types in an unrelated `Zeta.Hidden` namespace.
        // The legacy discovery filters those out before computing the assembly
        // root namespace, so the prefix must stay at "Acme.Mixed" — without the
        // filter it would shrink to "" and break import subpath generation.
        var lib = TranspileHelper.CompileLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-mixed")]

            namespace Acme.Mixed.Feature
            {
                [Transpile]
                public class Real {}
            }

            namespace Acme.Mixed.Other
            {
                [Transpile]
                public class Sibling {}
            }

            namespace Zeta.Hidden
            {
                [NoEmit]
                public class Ambient {}

                [NoTranspile]
                public class Ignored {}
            }
            """,
            assemblyName: "AcmeMixed"
        );

        var consumer = TranspileHelper.CompileConsumer(
            """
            [Transpile]
            public class Marker {}
            """,
            lib
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(consumer);

        var realKey = consumer
            .GetTypeByMetadataName("Acme.Mixed.Feature.Real")!
            .GetStableFullName();
        await Assert.That(ir.CrossAssemblyOrigins).ContainsKey(realKey);
        await Assert
            .That(ir.CrossAssemblyOrigins[realKey].AssemblyRootNamespace)
            .IsEqualTo("Acme.Mixed");
    }

    [Test]
    public async Task ExternalImports_NameCollisionKeepsFirstAndWarns()
    {
        // Two top-level types share the simple name `Widget` across distinct
        // namespaces with conflicting [Import] mappings. CollectPublicTopLevelTypes
        // walks namespaces via INamespaceSymbol.GetMembers(), which Roslyn returns
        // in declaration/alphabetical order — `Alpha` is visited before `Beta`,
        // so the Alpha mapping wins and the Beta one becomes a warning.
        var compilation = IrTestHelper.Compile(
            """
            namespace Alpha
            {
                [Import("Widget", from: "alpha-pkg")]
                public class Widget {}
            }

            namespace Beta
            {
                [Import("Widget", from: "beta-pkg")]
                public class Widget {}
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.ExternalImports.Count).IsEqualTo(1);
        await Assert.That(ir.ExternalImports).ContainsKey("Widget");
        var entry = ir.ExternalImports["Widget"];
        await Assert.That(entry.From).IsEqualTo("alpha-pkg");

        var warning = ir.Diagnostics.SingleOrDefault(d =>
            d.Severity == MetanoDiagnosticSeverity.Warning
            && d.Code == DiagnosticCodes.AmbiguousConstruct
        );
        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.Message).Contains("Widget");
        await Assert.That(warning.Message).Contains("beta-pkg");
    }
}
