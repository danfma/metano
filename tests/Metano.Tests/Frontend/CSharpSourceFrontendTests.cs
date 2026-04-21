using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
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
    public async Task ExternalImports_RegisterTargetSpecificNameAlias()
    {
        // The type's C# source identifier is HonoStub but its TS-facing
        // [Name(TypeScript, "Hono")] alias is what user code emits when the
        // import is resolved — the frontend must register both keys so the
        // backend can look the entry up by either name without re-walking
        // Roslyn for the alias.
        var compilation = IrTestHelper.Compile(
            """
            [Import("Hono", from: "hono")]
            [Name(TargetLanguage.TypeScript, "Hono")]
            public class HonoStub {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.ExternalImports).ContainsKey("HonoStub");
        await Assert.That(ir.ExternalImports).ContainsKey("Hono");
        await Assert.That(ir.ExternalImports["HonoStub"]).IsEqualTo(ir.ExternalImports["Hono"]);
    }

    [Test]
    public async Task ExternalImports_IgnoreAliasForOtherTarget()
    {
        // A [Name(Dart, …)] override must not leak into the TS run — the
        // frontend is invoked with TargetLanguage.TypeScript (the test
        // default) so only the C# source name key exists on the map.
        var compilation = IrTestHelper.Compile(
            """
            [Import("Widget", from: "flutter/widgets")]
            [Name(TargetLanguage.Dart, "FlutterWidget")]
            public class Widget {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.ExternalImports).ContainsKey("Widget");
        await Assert.That(ir.ExternalImports.ContainsKey("FlutterWidget")).IsFalse();
    }

    [Test]
    public async Task ExternalImports_UseTargetSpecificAliasWhenExtractingForDart()
    {
        // When the frontend is invoked for Dart, the same type's Dart alias
        // wins and its TS alias is ignored — mirror of the case above so
        // target awareness is exercised in both directions.
        var compilation = IrTestHelper.Compile(
            """
            [Import("Widget", from: "flutter/widgets")]
            [Name(TargetLanguage.TypeScript, "TsWidget")]
            [Name(TargetLanguage.Dart, "FlutterWidget")]
            public class Widget {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            TargetLanguage.Dart
        );

        await Assert.That(ir.ExternalImports).ContainsKey("Widget");
        await Assert.That(ir.ExternalImports).ContainsKey("FlutterWidget");
        await Assert.That(ir.ExternalImports.ContainsKey("TsWidget")).IsFalse();
    }

    [Test]
    public async Task ExternalImports_UntargetedNameAliasRegistersRegardlessOfTarget()
    {
        // An untargeted [Name("X")] applies to every target via
        // SymbolHelper.GetNameOverride's fallback branch, so both a
        // TypeScript and a Dart extraction must register the same alias
        // alongside the C# source name.
        var source = """
            [Import("Widget", from: "pkg")]
            [Name("AliasedWidget")]
            public class Widget {}
            """;

        var tsIr = new CSharpSourceFrontend().ExtractFromCompilation(IrTestHelper.Compile(source));
        await Assert.That(tsIr.ExternalImports).ContainsKey("Widget");
        await Assert.That(tsIr.ExternalImports).ContainsKey("AliasedWidget");

        var dartIr = new CSharpSourceFrontend().ExtractFromCompilation(
            IrTestHelper.Compile(source),
            TargetLanguage.Dart
        );
        await Assert.That(dartIr.ExternalImports).ContainsKey("Widget");
        await Assert.That(dartIr.ExternalImports).ContainsKey("AliasedWidget");
    }

    [Test]
    public async Task ExternalImports_AliasCollisionRaisesMS0003()
    {
        // Two [Import] types collide on the TS alias: the first wins and
        // the second registration produces MS0003 with the alias key in
        // the diagnostic message.
        var compilation = IrTestHelper.Compile(
            """
            namespace Alpha
            {
                [Import("FirstWidget", from: "alpha")]
                [Name(TargetLanguage.TypeScript, "SharedWidget")]
                public class AlphaWidget {}
            }

            namespace Beta
            {
                [Import("SecondWidget", from: "beta")]
                [Name(TargetLanguage.TypeScript, "SharedWidget")]
                public class BetaWidget {}
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.ExternalImports).ContainsKey("SharedWidget");
        // First walker visit wins. CollectTopLevelTypes visits namespaces
        // in declaration/alphabetical order, so Alpha wins.
        await Assert.That(ir.ExternalImports["SharedWidget"].From).IsEqualTo("alpha");

        var warning = ir.Diagnostics.SingleOrDefault(d =>
            d.Code == DiagnosticCodes.AmbiguousConstruct && d.Message.Contains("SharedWidget")
        );
        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.Message).Contains("beta");
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
        var key = moneyType!.GetCrossAssemblyOriginKey();

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
        await Assert.That(ir.CrossAssemblyOrigins.Keys).DoesNotContain("OrphanLib:Orphan.Detached");
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

        var realKey = consumer
            .GetTypeByMetadataName("Acme.Mixed.Real")!
            .GetCrossAssemblyOriginKey();
        var honoKey = consumer
            .GetTypeByMetadataName("Acme.Mixed.HonoStub")!
            .GetCrossAssemblyOriginKey();
        var ambientKey = consumer
            .GetTypeByMetadataName("Acme.Mixed.Ambient")!
            .GetCrossAssemblyOriginKey();

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
            .GetCrossAssemblyOriginKey();
        await Assert.That(ir.CrossAssemblyOrigins).ContainsKey(realKey);
        await Assert
            .That(ir.CrossAssemblyOrigins[realKey].AssemblyRootNamespace)
            .IsEqualTo("Acme.Mixed");
    }

    [Test]
    public async Task PackageName_ReadsEmitPackageFromCurrentAssembly()
    {
        var compilation = IrTestHelper.Compile(
            """
            [assembly: EmitPackage("my-pkg")]

            [Transpile]
            public class Marker {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.PackageName).IsEqualTo("my-pkg");
    }

    [Test]
    public async Task PackageName_NullWhenAttributeAbsent()
    {
        var compilation = IrTestHelper.Compile(
            """
            [Transpile]
            public class Marker {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.PackageName).IsNull();
    }

    [Test]
    public async Task AssemblyWideTranspile_TrueWhenAttributeDeclared()
    {
        var compilation = IrTestHelper.Compile(
            """
            [assembly: TranspileAssembly]

            public class PublicNoAttribute {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.AssemblyWideTranspile).IsTrue();
    }

    [Test]
    public async Task AssemblyWideTranspile_FalseWhenAttributeAbsent()
    {
        var compilation = IrTestHelper.Compile(
            """
            [Transpile]
            public class Marker {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.AssemblyWideTranspile).IsFalse();
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

    [Test]
    public async Task ExternalImports_IncludesCrossAssemblyEntriesByCSharpName()
    {
        var lib = TranspileHelper.CompileLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-shared")]

            namespace Acme.Shared.External
            {
                [Import("Hono", from: "hono")]
                public class HonoStub {}
            }
            """,
            assemblyName: "AcmeShared"
        );

        var consumer = TranspileHelper.CompileConsumer(
            """
            [Transpile]
            public class Marker {}
            """,
            lib
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(consumer);

        await Assert.That(ir.ExternalImports).ContainsKey("HonoStub");
        var entry = ir.ExternalImports["HonoStub"];
        await Assert.That(entry.Name).IsEqualTo("Hono");
        await Assert.That(entry.From).IsEqualTo("hono");
    }

    [Test]
    public async Task ExternalImports_SkipsReferencesWithoutTranspileAssembly()
    {
        // A plain C# library without [TranspileAssembly] must NOT contribute
        // its [Import] types to the consumer — even if an npm package happens
        // to exist — so unrelated libraries cannot leak bindings.
        var lib = TranspileHelper.CompileLibrary(
            """
            using Metano.Annotations;

            namespace Plain.Library
            {
                [Import("Ghost", from: "ghost-pkg")]
                public class Ghost {}
            }
            """,
            assemblyName: "PlainLib"
        );

        var consumer = TranspileHelper.CompileConsumer(
            """
            [Transpile]
            public class Marker {}
            """,
            lib
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(consumer);

        await Assert.That(ir.ExternalImports.ContainsKey("Ghost")).IsFalse();
    }

    [Test]
    public async Task ExternalImports_SkipsReferencesWithoutEmitPackage()
    {
        // A library that opts into transpilation but omits [EmitPackage]
        // for the active target is already flagged via
        // AssembliesNeedingEmitPackage — its [Import] types must not leak
        // either so the consumer does not quietly pick up half-configured
        // bindings.
        var lib = TranspileHelper.CompileLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Orphan.Library
            {
                [Import("Ghost", from: "ghost-pkg")]
                public class Ghost {}
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

        await Assert.That(ir.ExternalImports.ContainsKey("Ghost")).IsFalse();
        await Assert.That(ir.AssembliesNeedingEmitPackage).Contains("OrphanLib");
    }

    [Test]
    public async Task ExternalImports_LocalEntryWinsOverCrossAssemblyCollision()
    {
        // Two [Import("Widget", ...)] attributes collide on the simple name
        // "Widget" — one in the consumer, one in a transpilable library.
        // The local mapping must win regardless of enumeration order, so
        // the consumer can always override bindings shipped by a library.
        var lib = TranspileHelper.CompileLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Acme.Lib
            {
                [Import("Widget", from: "lib-widget")]
                public class Widget {}
            }
            """,
            assemblyName: "AcmeLib"
        );

        var consumer = TranspileHelper.CompileConsumer(
            """
            [Import("Widget", from: "local-widget")]
            public class Widget {}

            [Transpile]
            public class Marker {}
            """,
            lib
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(consumer);

        await Assert.That(ir.ExternalImports).ContainsKey("Widget");
        var entry = ir.ExternalImports["Widget"];
        await Assert.That(entry.From).IsEqualTo("local-widget");

        var warning = ir.Diagnostics.SingleOrDefault(d =>
            d.Code == DiagnosticCodes.AmbiguousConstruct && d.Message.Contains("Widget")
        );
        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.Message).Contains("local-widget");
        await Assert.That(warning.Message).Contains("lib-widget");
    }

    [Test]
    public async Task LocalRootNamespace_EmptyWhenNoTranspilableTypes()
    {
        var compilation = IrTestHelper.Compile(
            """
            public class NotMarked {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.LocalRootNamespace).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task LocalRootNamespace_ReturnsSingleNamespaceWhenOnlyOneInUse()
    {
        var compilation = IrTestHelper.Compile(
            """
            namespace Acme.Shared.Domain
            {
                [Transpile]
                public class Money {}
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.LocalRootNamespace).IsEqualTo("Acme.Shared.Domain");
    }

    [Test]
    public async Task LocalRootNamespace_ReturnsLongestCommonPrefixAcrossNamespaces()
    {
        var compilation = IrTestHelper.Compile(
            """
            namespace Acme.Shared.Domain
            {
                [Transpile]
                public class Money {}
            }

            namespace Acme.Shared.Services
            {
                [Transpile]
                public class PaymentProcessor {}
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.LocalRootNamespace).IsEqualTo("Acme.Shared");
    }

    [Test]
    public async Task LocalRootNamespace_IgnoresNoTranspileAndNoEmit()
    {
        // [NoTranspile] / [NoEmit] types live under a wildly different
        // namespace. If the filter leaked them, the common prefix would
        // collapse to the empty string.
        var compilation = IrTestHelper.Compile(
            """
            namespace Acme.Shared.Domain
            {
                [Transpile]
                public class Money {}
            }

            namespace Unrelated.Zeta
            {
                [NoTranspile]
                public class Ignored {}

                [NoEmit]
                public class Ambient {}
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.LocalRootNamespace).IsEqualTo("Acme.Shared.Domain");
    }

    [Test]
    public async Task LocalRootNamespace_AssemblyWideSkipsNonPublicTypes()
    {
        // Assembly-wide mode only transpiles public types; an `internal`
        // type in an unrelated namespace must not collapse the prefix.
        var compilation = IrTestHelper.Compile(
            """
            [assembly: TranspileAssembly]

            namespace Acme.Shared
            {
                public class Money {}
            }

            namespace Unrelated.Zeta
            {
                internal class Hidden {}
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.LocalRootNamespace).IsEqualTo("Acme.Shared");
    }

    [Test]
    public async Task LocalRootNamespace_ExplicitTranspileIncludesInternalTypes()
    {
        // [Transpile] overrides the public-only gate, so an internal type
        // decorated with it must count towards the prefix — matching the
        // target-side IsTranspilable semantics.
        var compilation = IrTestHelper.Compile(
            """
            namespace Acme.Shared.Domain
            {
                [Transpile]
                internal class Money {}
            }

            namespace Acme.Shared.Services
            {
                [Transpile]
                public class PaymentProcessor {}
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.LocalRootNamespace).IsEqualTo("Acme.Shared");
    }

    [Test]
    public async Task LocalRootNamespace_IncludesSyntheticProgramFromTopLevelStatements()
    {
        // C# 9+ top-level statements inside a namespace compile into a
        // synthesized `Program` type under that namespace. TypeTransformer
        // treats it as transpilable under [assembly: TranspileAssembly];
        // the frontend must contribute its namespace to the common prefix
        // so the generated file layout matches.
        var compilation = IrTestHelper.Compile(
            """
            [assembly: TranspileAssembly]

            System.Console.WriteLine("hello");

            namespace Acme.Shared.Services
            {
                public class Marker {}
            }
            """,
            outputKind: Microsoft.CodeAnalysis.OutputKind.ConsoleApplication
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.LocalRootNamespace).IsEqualTo("Acme.Shared.Services");
    }

    [Test]
    public async Task LocalRootNamespace_SkipsGlobalNamespaceTypes()
    {
        // A transpilable type declared directly in the global namespace
        // has no segments to contribute; the prefix is driven purely by
        // the namespaced transpilable types.
        var compilation = IrTestHelper.Compile(
            """
            [Transpile]
            public class Marker {}

            namespace Acme.Shared.Domain
            {
                [Transpile]
                public class Money {}
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.LocalRootNamespace).IsEqualTo("Acme.Shared.Domain");
    }

    // Tests below declare their own user types so the assertions aren't
    // diluted by the many [MapMethod]/[MapProperty] declarations Metano.Runtime
    // ships for BCL types like List<T>, Guid, Dictionary, etc.

    [Test]
    public async Task DeclarativeMethodMappings_IndexesCurrentAssemblyMapMethod()
    {
        var compilation = IrTestHelper.Compile(
            """
            [assembly: MapMethod(
                typeof(AcmeUnique.Mappings.UniqueWidget),
                "Execute",
                JsMethod = "run"
            )]

            namespace AcmeUnique.Mappings
            {
                public class UniqueWidget
                {
                    public void Execute() {}
                }
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.DeclarativeMethodMappings).IsNotNull();
        var key = ("AcmeUnique.Mappings.UniqueWidget", "Execute");
        await Assert.That(ir.DeclarativeMethodMappings!).ContainsKey(key);
        var entries = ir.DeclarativeMethodMappings![key];
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].JsName).IsEqualTo("run");
    }

    [Test]
    public async Task DeclarativeMethodMappings_GroupsOverloadsByLiteralFilter()
    {
        var compilation = IrTestHelper.Compile(
            """
            [assembly: MapMethod(typeof(AcmeUnique.Mappings.Formatter), "Render",
                JsTemplate = "$0.render($1)")]
            [assembly: MapMethod(typeof(AcmeUnique.Mappings.Formatter), "Render",
                WhenArg0StringEquals = "fast",
                JsTemplate = "$0.fastRender()")]

            namespace AcmeUnique.Mappings
            {
                public class Formatter
                {
                    public string Render(string mode) => "";
                }
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        var key = ("AcmeUnique.Mappings.Formatter", "Render");
        await Assert.That(ir.DeclarativeMethodMappings!).ContainsKey(key);
        var entries = ir.DeclarativeMethodMappings![key];
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries[0].WhenArg0StringEquals).IsNull();
        await Assert.That(entries[1].WhenArg0StringEquals).IsEqualTo("fast");
    }

    [Test]
    public async Task DeclarativePropertyMappings_IndexesMapProperty()
    {
        var compilation = IrTestHelper.Compile(
            """
            [assembly: MapProperty(
                typeof(AcmeUnique.Mappings.Container),
                "ItemCount",
                JsProperty = "size"
            )]

            namespace AcmeUnique.Mappings
            {
                public class Container
                {
                    public int ItemCount => 0;
                }
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        var key = ("AcmeUnique.Mappings.Container", "ItemCount");
        await Assert.That(ir.DeclarativePropertyMappings!).ContainsKey(key);
        var entry = ir.DeclarativePropertyMappings![key];
        await Assert.That(entry.JsName).IsEqualTo("size");
    }

    [Test]
    public async Task ChainMethodsByWrapper_CollectsJsNamesByWrapper()
    {
        var compilation = IrTestHelper.Compile(
            """
            [assembly: MapMethod(typeof(AcmeUnique.Mappings.Widget), "Add",
                JsMethod = "appendThing", WrapReceiver = "uniqueWrap")]
            [assembly: MapMethod(typeof(AcmeUnique.Mappings.Widget), "Remove",
                JsMethod = "dropThing", WrapReceiver = "uniqueWrap")]
            [assembly: MapMethod(typeof(AcmeUnique.Mappings.Widget), "Sort",
                JsTemplate = "$0.reorderThings()", WrapReceiver = "uniqueWrap")]

            namespace AcmeUnique.Mappings
            {
                public class Widget
                {
                    public void Add() {}
                    public void Remove() {}
                    public void Sort() {}
                }
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.ChainMethodsByWrapper).IsNotNull();
        await Assert.That(ir.ChainMethodsByWrapper!).ContainsKey("uniqueWrap");
        var names = ir.ChainMethodsByWrapper!["uniqueWrap"];
        // JsTemplate-only entries are excluded — wrap detection needs a JsName.
        await Assert.That(names).Contains("appendThing");
        await Assert.That(names).Contains("dropThing");
        await Assert.That(names.Contains("reorderThings")).IsFalse();
    }

    [Test]
    public async Task DeclarativeMappings_MutuallyExclusiveJsNameAndJsTemplateWarns()
    {
        // Set both JsMethod (rename) and JsTemplate on the same attribute:
        // the frontend must surface MS0004 and keep JsTemplate, so the
        // target never sees a mapping entry with both populated.
        var compilation = IrTestHelper.Compile(
            """
            [assembly: MapMethod(typeof(AcmeUnique.Mappings.Conflicted), "Run",
                JsMethod = "run", JsTemplate = "$0.run()")]

            namespace AcmeUnique.Mappings
            {
                public class Conflicted
                {
                    public void Run() {}
                }
            }
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        var warning = ir.Diagnostics.SingleOrDefault(d =>
            d.Code == DiagnosticCodes.ConflictingAttributes
            && d.Message.Contains("Conflicted")
            && d.Message.Contains("Run")
        );
        await Assert.That(warning).IsNotNull();

        var key = ("AcmeUnique.Mappings.Conflicted", "Run");
        await Assert.That(ir.DeclarativeMethodMappings!).ContainsKey(key);
        var entry = ir.DeclarativeMethodMappings![key][0];
        await Assert.That(entry.JsName).IsNull();
        await Assert.That(entry.JsTemplate).IsEqualTo("$0.run()");
    }

    [Test]
    public async Task DeclarativeMappings_DictionaryHandlesPresentEvenWhenCurrentAssemblyHasNoAttributes()
    {
        // Metano.Runtime is on every consumer's reference set, so the
        // inherited tables are never empty. This test just pins the IR
        // contract: the dictionaries are always non-null after extraction,
        // so callers can safely do `ir.DeclarativeMethodMappings ?? empty`.
        var compilation = IrTestHelper.Compile(
            """
            public class Marker {}
            """
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);

        await Assert.That(ir.DeclarativeMethodMappings).IsNotNull();
        await Assert.That(ir.DeclarativePropertyMappings).IsNotNull();
        await Assert.That(ir.ChainMethodsByWrapper).IsNotNull();
    }
}
