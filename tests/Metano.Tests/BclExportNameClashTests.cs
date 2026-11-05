using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A transpilable type whose emitted name matches a BCL export's name — a
/// user <c>Numerics.Decimal</c> against <c>System.Decimal</c>'s
/// <c>Decimal</c> from <c>decimal.js</c>, or a user <c>MidpointRounding</c>
/// against the runtime's enum mirror — resolves to its own generated module
/// like any other transpiled type (issue #477): only the full BCL symbol
/// maps to the package, through an origin each emitted reference carries,
/// so the collector imports decimal.js / metano-runtime only for those
/// nodes; a same-file collision reports MS0070 instead of binding both to
/// one module.
/// </summary>
public class BclExportNameClashTests
{
    private const string NumericsSource = """
        namespace App;

        [Transpile]
        public struct Decimal
        {
            public int Raw { get; set; }
        }

        [Transpile]
        public sealed class Calculator
        {
            public App.Decimal Sum(App.Decimal left, App.Decimal right) => left;
        }
        """;

    [Test]
    public async Task UserDecimal_EmitsItsOwnModule()
    {
        var result = TranspileHelper.Transpile(NumericsSource);

        await Assert.That(result["app/decimal.ts"]).Contains("export class Decimal {");
    }

    [Test]
    public async Task UserDecimal_IsImportedFromItsOwnModule_NotDecimalJs()
    {
        var result = TranspileHelper.Transpile(NumericsSource);

        var output = result["app/calculator.ts"];

        await Assert.That(output).Contains("import type { Decimal } from \"./decimal\";");
        await Assert.That(output).DoesNotContain("decimal.js");
    }

    [Test]
    public async Task UserDecimal_GoldenMatchesTheExpectedOutput()
    {
        var result = TranspileHelper.Transpile(NumericsSource);

        await Assert
            .That(result["app/calculator.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("bcl-name-clash-decimal.ts"));
    }

    [Test]
    public async Task UserDecimal_AsValue_ImportsTheProjectTypeForConstruction()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public struct Decimal
            {
                public int Raw { get; set; }
            }

            [Transpile]
            public sealed class Calculator
            {
                public App.Decimal One() => new App.Decimal { Raw = 1 };
            }
            """
        );

        var output = result["app/calculator.ts"];

        await Assert.That(output).Contains("import { Decimal } from \"./decimal\";");
        await Assert.That(output).Contains("})(new Decimal());");
        await Assert.That(output).DoesNotContain("decimal.js");
    }

    [Test]
    public async Task UserMidpointRounding_IsImportedFromItsOwnModule_NotTheRuntime()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public enum MidpointRounding { AwayFromZero, ToEven }

            [Transpile]
            public sealed class Rounder
            {
                public App.MidpointRounding Mode() => App.MidpointRounding.ToEven;
            }
            """
        );

        var output = result["app/rounder.ts"];

        // The enum member read (`MidpointRounding.ToEven`) is a value use, so
        // the import is a plain one — from the project's own module, not the
        // runtime's `MidpointRounding` mirror the BCL export binds.
        await Assert
            .That(output)
            .Contains("import { MidpointRounding } from \"./midpoint-rounding\";");
        await Assert
            .That(output)
            .DoesNotContain("import { MidpointRounding } from \"metano-runtime\";");
    }

    /// <summary>
    /// A project type named <c>Decimal</c> does not detach the real BCL
    /// mapping: a file that uses only the C# <c>decimal</c> keeps importing
    /// decimal.js through the export's origin node.
    /// </summary>
    [Test]
    public async Task BclDecimal_StillImportsDecimalJs_WhenAProjectTypeSharesTheName()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Other
            {
                [Transpile]
                public struct Decimal
                {
                    public int Raw { get; set; }
                }
            }

            namespace App
            {
                [Transpile]
                public sealed class Money
                {
                    public decimal Sum(decimal left, decimal right) => left + right;
                }
            }
            """
        );

        var output = result["app/money.ts"];

        await Assert.That(output).Contains("import type { Decimal } from \"decimal.js\";");
        await Assert.That(output).Contains("left.plus(right)");
    }

    /// <summary>
    /// A project enum named <c>MidpointRounding</c> does not detach the BCL
    /// mapping either: `Math.Round` with `System.MidpointRounding.AwayFromZero`
    /// keeps importing the runtime's mirror enum.
    /// </summary>
    [Test]
    public async Task BclMidpointRounding_StillImportsTheRuntimeMirror_WhenAProjectTypeSharesTheName()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Other
            {
                [Transpile]
                public enum MidpointRounding { X, Y, Z }
            }

            namespace App
            {
                [Transpile]
                public sealed class Rounder
                {
                    public double Away(double a) => Math.Round(a, System.MidpointRounding.AwayFromZero);
                }
            }
            """
        );

        var output = result["app/rounder.ts"];

        await Assert
            .That(output)
            .Contains("import { MidpointRounding, roundNumber } from \"metano-runtime\";");
    }

    /// <summary>
    /// Cross-package and BCL share the name without stealing each other's
    /// module: each file's import follows its own reference's origin.
    /// </summary>
    [Test]
    public async Task CrossPackageUserDecimal_AndBclDecimal_BindTheirOwnModules()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/numerics")]

            namespace Lib
            {
                public struct Decimal
                {
                    public int Raw { get; set; }
                }
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App
            {
                [Transpile]
                public sealed class Money
                {
                    public Lib.Decimal Convert(Lib.Decimal value) => value;
                }

                [Transpile]
                public sealed class Calc
                {
                    public decimal Sum(decimal left, decimal right) => left + right;
                }
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);

        await Assert
            .That(result["app/money.ts"])
            .Contains("import type { Decimal } from \"@scope/numerics/lib\";");
        await Assert.That(result["app/money.ts"]).DoesNotContain("decimal.js");
        await Assert
            .That(result["app/calc.ts"])
            .Contains("import type { Decimal } from \"decimal.js\";");
    }

    /// <summary>
    /// A file that references both the decimal.js export and a project type
    /// of the same name reports MS0070 — the bare identifier would bind both
    /// imports, so one is wrong; the diagnostic tells the author to rename.
    /// </summary>
    [Test]
    public async Task FileReferencingUserAndBclDecimal_ReportsMS0070()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public struct Decimal
            {
                public int Raw { get; set; }
            }

            [Transpile]
            public sealed class Money
            {
                public App.Decimal Wrap(decimal value) => default;
            }
            """
        );

        var clashes = diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash).ToList();

        await Assert.That(clashes).HasSingleItem();
        await Assert
            .That(clashes[0].Message)
            .Contains("'Decimal' names both an import from 'decimal.js' and a project type in");
    }

    /// <summary>
    /// A cross-package type and the BCL export of the same name in one file
    /// clash the same way — two origins staged for one identifier — and the
    /// diagnostic names both packages. The first origin keeps the binding.
    /// </summary>
    [Test]
    public async Task CrossPackageUserDecimal_AndBclDecimal_InOneFile_ReportsMS0070()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/numerics")]

            namespace Lib
            {
                public struct Decimal
                {
                    public int Raw { get; set; }
                }
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App
            {
                [Transpile]
                public sealed class Money
                {
                    public Lib.Decimal Wrap(decimal value) => default;
                }
            }
            """;

        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            library,
            consumer
        );

        var clashes = diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash).ToList();

        await Assert.That(clashes).HasSingleItem();
        await Assert
            .That(clashes[0].Message)
            .Contains("'Decimal' names both an import from 'decimal.js' and an import from");
    }

    /// <summary>
    /// Bare BCL identifiers do not invent a project binding: a file whose
    /// only `Decimal` tokens are the overload dispatch's `instanceof` checks
    /// imports decimal.js and raises no MS0070, whatever other files declare.
    /// </summary>
    [Test]
    public async Task DecimalOverloadDispatch_WithAProjectDecimalElsewhere_HasNoClash()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App.Other
            {
                [Transpile]
                public struct Decimal
                {
                    public int Raw { get; set; }
                }
            }

            namespace App
            {
                [Transpile]
                public sealed class Money
                {
                    public decimal Sum(decimal left) => left;

                    public decimal Sum(string raw) => decimal.Parse(raw);
                }
            }
            """
        );

        var output = result["app/money.ts"];

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();
        await Assert.That(output).Contains("import { Decimal } from \"decimal.js\";");
        await Assert.That(output).Contains("instanceof Decimal");
    }

    /// <summary>
    /// An `[ExportFromBcl]` that names no package imports nothing, like the
    /// base emitter: the decimal use maps to its primitive instead of an
    /// `import … from ""` line.
    /// </summary>
    [Test]
    public async Task BclExportWithoutAPackage_ImportsNothing()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: Metano.Annotations.ExportFromBcl(
                typeof(decimal),
                ExportedName = "Big"
            )]

            namespace App;

            [Transpile]
            public sealed class Money
            {
                public decimal Sum(decimal left, decimal right) => left + right;
            }
            """
        );

        var output = result["app/money.ts"];

        await Assert.That(output).DoesNotContain("from \"\"");
        await Assert.That(output).DoesNotContain("decimal.js");
    }
}
