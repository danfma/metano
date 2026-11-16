using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for <c>MS0020 NoContainerFactoryNameClash</c>. The diagnostic
/// fires when an <c>[NoContainer]</c> static method's emitted TS name —
/// after <c>[Name]</c> resolution, otherwise camelCase — collides with
/// the TS name of a transpilable type the same emit scope can see, or
/// with another <c>[NoContainer]</c> factory across classes. Without the
/// diagnostic the import collector silently resolves the bare
/// identifier to the factory instead of the class, the factory body's
/// <c>new ClassName(...)</c> becomes a recursive call, and consumer
/// files import the function where they meant the type.
/// </summary>
public class NoContainerFactoryNameClashTests
{
    [Test]
    public async Task NameOverride_MatchingTranspilableType_AutoAliasesAndEmitsMs0022()
    {
        // Stage 2 of #181: a factory shadowing a transpilable type used
        // to raise MS0020 (Error) and abort the export. The auto-alias
        // path now synthesizes a path-derived alias and downgrades the
        // diagnostic to MS0022 (Info) so the export survives. MS0020
        // still fires for non-transpilable collisions (BCL, [Import],
        // runtime helper) — covered by sibling tests.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            namespace App;

            [Transpile]
            public sealed class Column
            {
                public Column(int gap) { }
            }

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs, Name("Column")]
                public static Column Column(int gap) => new(gap);
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.NoContainerFactoryNameClash))
            .IsFalse();

        var ms0022 = diagnostics.FirstOrDefault(d =>
            d.Code == DiagnosticCodes.AliasedImportConflict
        );
        await Assert.That(ms0022).IsNotNull();
        await Assert.That(ms0022!.Message).Contains("Column");
        await Assert.That(ms0022.Message).Contains("UI.Column");
    }

    [Test]
    public async Task NameOverride_NotMatchingAnyType_NoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            namespace App;

            [Transpile]
            public sealed class Column
            {
                public Column(int gap) { }
            }

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs, Name("makeColumn")]
                public static Column Column(int gap) => new(gap);
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.NoContainerFactoryNameClash))
            .IsFalse();
    }

    [Test]
    public async Task DefaultCamelCase_DoesNotClashWithPascalCaseType_NoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            namespace App;

            [Transpile]
            public sealed class Column
            {
                public Column(int gap) { }
            }

            [Transpile, NoContainer]
            public static class UI
            {
                [ObjectArgs]
                public static Column Column(int gap) => new(gap);
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.NoContainerFactoryNameClash))
            .IsFalse();
    }

    // Cross-assembly clash detection follows the cross-assembly NoContainer
    // discovery work in issue #178; today the detector scans only the
    // current assembly's transpilable type table.

    [Test]
    public async Task NameOverride_MatchingRuntimeHelper_RaisesMs0020()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            namespace App;

            [Transpile, NoContainer]
            public static class Codecs
            {
                [Name("Grouping")]
                public static int Wrap(int x) => x;
            }
            """
        );

        var ms0020 = diagnostics.FirstOrDefault(d =>
            d.Code == DiagnosticCodes.NoContainerFactoryNameClash
        );
        await Assert.That(ms0020).IsNotNull();
        await Assert.That(ms0020!.Message).Contains("metano-runtime");
    }

    [Test]
    public async Task TwoNoContainerFactoriesSameName_RaisesMs0020()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            namespace App;

            [Transpile, NoContainer]
            public static class Containers
            {
                [Name("box")]
                public static int OneBox(int x) => x;
            }

            [Transpile, NoContainer]
            public static class Wrappers
            {
                [Name("box")]
                public static int AnotherBox(int x) => x;
            }
            """
        );

        var ms0020 = diagnostics.FirstOrDefault(d =>
            d.Code == DiagnosticCodes.NoContainerFactoryNameClash
        );
        await Assert.That(ms0020).IsNotNull();
        await Assert.That(ms0020!.Message).Contains("box");
    }
}
