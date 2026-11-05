namespace Metano.Tests;

/// <summary>
/// A transpilable type whose emitted name matches a BCL export's name — a
/// user <c>Numerics.Decimal</c> against <c>System.Decimal</c>'s
/// <c>Decimal</c> from <c>decimal.js</c>, or a user <c>MidpointRounding</c>
/// against the runtime's enum mirror — resolves to its own generated module
/// like any other transpiled type (issue #477): the import collector claimed
/// every name equal to a BCL export's <c>ExportedName</c> for the package,
/// whatever type it belonged to.
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
        await Assert.That(output).DoesNotContain("metano-runtime");
    }
}
