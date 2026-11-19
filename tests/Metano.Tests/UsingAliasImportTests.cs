namespace Metano.Tests;

public class UsingAliasImportTests
{
    [Test]
    public async Task UsingAlias_AppliesToReturnTypeAndImport()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            using ColumnWidget = App.Widgets.Column;

            namespace App.Widgets
            {
                [Transpile]
                public class Column
                {
                    public int Gap { get; }
                    public Column(int gap) { Gap = gap; }
                }
            }

            namespace App
            {
                [Transpile]
                public class Ui
                {
                    public ColumnWidget Make() => new ColumnWidget(12);
                }
            }
            """
        );

        var output = result["app/ui.ts"];
        await Assert.That(output).Contains("import { Column as ColumnWidget }");
        await Assert.That(output).Contains("new ColumnWidget(12)");
    }

    [Test]
    public async Task UsingAlias_ResolvesNameClashWithLocalFactory()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            using ColumnWidget = App.Widgets.Column;

            namespace App.Widgets
            {
                [Transpile]
                public class Column
                {
                    public int Gap { get; }
                    public Column(int gap) { Gap = gap; }
                }
            }

            namespace App
            {
                [Transpile, NoContainer]
                public static class Ui
                {
                    [Name(TargetLanguage.TypeScript, "Column")]
                    public static ColumnWidget Column(int gap) => new ColumnWidget(gap);
                }
            }
            """
        );

        var output = result["app/ui.ts"];
        await Assert.That(output).Contains("import { Column as ColumnWidget }");
        await Assert.That(output).Contains("export function Column(gap: number): ColumnWidget");
        await Assert.That(output).Contains("return new ColumnWidget(gap);");
    }

    [Test]
    public async Task NoContainerFactoryNameClash_AutoAliasesAndEmitsInfo()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            namespace App.Widgets
            {
                [Transpile]
                public class Column
                {
                    public int Gap { get; }
                    public Column(int gap) { Gap = gap; }
                }
            }

            namespace App
            {
                [Transpile, NoContainer]
                public static class Ui
                {
                    [Name(TargetLanguage.TypeScript, "Column")]
                    public static App.Widgets.Column Column(int gap) => new App.Widgets.Column(gap);
                }
            }
            """
        );

        var output = result["app/ui.ts"];
        await Assert.That(output).Contains("import { Column as ColumnFromWidgets }");

        await Assert
            .That(output)
            .Contains("export function Column(gap: number): ColumnFromWidgets");

        await Assert.That(output).Contains("return new ColumnFromWidgets(gap);");

        var info = diagnostics.FirstOrDefault(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.AliasedImportConflict
        );

        await Assert.That(info).IsNotNull();

        await Assert
            .That(info!.Severity)
            .IsEqualTo(Metano.Compiler.Diagnostics.MetanoDiagnosticSeverity.Info);

        await Assert.That(info.Message).Contains("ColumnFromWidgets");
    }

    [Test]
    public async Task UsingAlias_OverridesAutoSynthesis()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            using ColumnWidget = App.Widgets.Column;

            namespace App.Widgets
            {
                [Transpile]
                public class Column
                {
                    public int Gap { get; }
                    public Column(int gap) { Gap = gap; }
                }
            }

            namespace App
            {
                [Transpile, NoContainer]
                public static class Ui
                {
                    [Name(TargetLanguage.TypeScript, "Column")]
                    public static ColumnWidget Column(int gap) => new ColumnWidget(gap);
                }
            }
            """
        );

        var output = result["app/ui.ts"];
        await Assert.That(output).Contains("import { Column as ColumnWidget }");
        await Assert.That(output).DoesNotContain("ColumnFromWidgets");

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.AliasedImportConflict
                )
            )
            .IsFalse();
    }

    [Test]
    public async Task ImportAliasAttribute_SingleType_AppliesPerFileAlias()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            namespace App.Widgets
            {
                [Transpile]
                public class Column
                {
                    public int Gap { get; }
                    public Column(int gap) { Gap = gap; }
                }
            }

            namespace App
            {
                [ImportAlias(typeof(App.Widgets.Column), "ColumnW")]
                file class TsModule;

                [Transpile, NoContainer]
                public static class Ui
                {
                    [Name(TargetLanguage.TypeScript, "Column")]
                    public static App.Widgets.Column Column(int gap) => new App.Widgets.Column(gap);
                }
            }
            """
        );

        var output = result["app/ui.ts"];
        await Assert.That(output).Contains("import { Column as ColumnW }");
        await Assert.That(output).Contains("export function Column(gap: number): ColumnW");
        await Assert.That(output).Contains("return new ColumnW(gap);");
        await Assert.That(result.Keys).DoesNotContain("app/ts-module.ts");
    }

    [Test]
    public async Task ImportAliasAttribute_BulkSuffix_AppliesToAllListedTypes()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            namespace App.Widgets
            {
                [Transpile]
                public class Row
                {
                    public Row() { }
                }

                [Transpile]
                public class Text
                {
                    public Text() { }
                }
            }

            namespace App
            {
                [ImportAlias(Suffix = "Widget", Types = new[] { typeof(App.Widgets.Row), typeof(App.Widgets.Text) })]
                file class TsModule;

                [Transpile]
                public class Caller
                {
                    public App.Widgets.Row MakeRow() => new App.Widgets.Row();
                    public App.Widgets.Text MakeText() => new App.Widgets.Text();
                }
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("Row as RowWidget");
        await Assert.That(output).Contains("Text as TextWidget");
        await Assert.That(output).Contains("return new RowWidget();");
        await Assert.That(output).Contains("return new TextWidget();");
    }

    [Test]
    public async Task ImportAliasAttribute_OverridesUsingAlias()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            using ColumnA = App.Widgets.Column;

            namespace App.Widgets
            {
                [Transpile]
                public class Column
                {
                    public int Gap { get; }
                    public Column(int gap) { Gap = gap; }
                }
            }

            namespace App
            {
                [ImportAlias(typeof(App.Widgets.Column), "ColumnB")]
                file class TsModule;

                [Transpile]
                public class Caller
                {
                    public ColumnA Make() => new ColumnA(12);
                }
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("import { Column as ColumnB }");
        await Assert.That(output).DoesNotContain("ColumnA");
    }

    [Test]
    public async Task ImportAliasAttribute_PerTargetFilter_NonMatchingDropped()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            namespace App.Widgets
            {
                [Transpile]
                public class Column
                {
                    public int Gap { get; }
                    public Column(int gap) { Gap = gap; }
                }
            }

            namespace App
            {
                [ImportAlias(TargetLanguage.Dart, typeof(App.Widgets.Column), "ColumnDart")]
                file class TsModule;

                [Transpile]
                public class Caller
                {
                    public App.Widgets.Column Make() => new App.Widgets.Column(12);
                }
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("import { Column }");
        await Assert.That(output).DoesNotContain("ColumnDart");
        await Assert.That(output).DoesNotContain(" as ");
    }

    [Test]
    public async Task NoUsingAlias_KeepsCanonicalImport()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            namespace App.Widgets
            {
                [Transpile]
                public class Column
                {
                    public int Gap { get; }
                    public Column(int gap) { Gap = gap; }
                }
            }

            namespace App
            {
                [Transpile]
                public class Caller
                {
                    public App.Widgets.Column Make() => new App.Widgets.Column(12);
                }
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("import { Column }");
        await Assert.That(output).DoesNotContain(" as ");
        await Assert.That(output).Contains("new Column(12)");
    }

    /// <summary>
    /// A <c>using</c> alias of a type from another <c>[EmitPackage]</c> package
    /// imports the export under the alias, in type and value positions, rather
    /// than importing the alias as a name the package does not export.
    /// </summary>
    [Test]
    public async Task UsingAlias_OfACrossPackageType_ImportsTheExportUnderTheAlias()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shared")]

            namespace Shared;

            public enum Status { Active, Inactive }
            """;

        var consumer = """
            using State = Shared.Status;

            [assembly: TranspileAssembly]

            namespace App;

            public class Order
            {
                public State Current { get; set; } = State.Active;
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(library, consumer)["app/order.ts"];

        await Assert.That(output).Contains("import { Status as State } from \"shared/shared\";");
        await Assert.That(output).Contains("State.Active");
        await Assert.That(output).DoesNotContain("import { State");
    }

    /// <summary>
    /// A <c>using</c> alias of a BCL-mapped type: <c>decimal</c> prints as
    /// decimal.js's <c>Decimal</c> in type and value positions alike (the
    /// mapping names the export, not the alias), so the alias is never
    /// imported as a name decimal.js does not export.
    /// </summary>
    [Test]
    public async Task UsingAlias_OfDecimal_ImportsDecimalOnly()
    {
        var output = TranspileHelper.Transpile(
            """
            using Money = System.Decimal;

            [Transpile]
            public static class Prices
            {
                public static Money Half(Money price) => price / 2;

                public static decimal Parse(string text) => Money.Parse(text);

                public static decimal Round(Money price) => Money.Round(price);
            }
            """
        )["prices.ts"];

        await Assert
            .That(output)
            .Contains("import { Decimal } from \"metano-runtime/system/decimal\";");

        await Assert.That(output).Contains("half(price: Decimal): Decimal");
        await Assert.That(output).Contains("return parseDecimal(text, Decimal);");
        await Assert.That(output).DoesNotContain("Money");
    }

    /// <summary>
    /// A guard called through an alias of its exception type lowers to the
    /// runtime function: neither the alias nor the exception class, which the
    /// template never prints, is imported.
    /// </summary>
    [Test]
    public async Task UsingAlias_OfAnExceptionType_ImportsNoClassForAGuard()
    {
        var output = TranspileHelper.Transpile(
            """
            using AE = System.ArgumentException;

            [Transpile]
            public static class Guard
            {
                public static void Check(string? text) => AE.ThrowIfNullOrEmpty(text);
            }
            """
        )["guard.ts"];

        await Assert.That(output).Contains("throwIfNullOrEmpty(text, \"text\");");
        await Assert.That(output).DoesNotContain("AE");
        await Assert.That(output).DoesNotContain("argument-exception");
    }

    /// <summary>
    /// One export bound under its own name and under a <c>using</c> alias in
    /// one file keeps both bindings: decimal.js's pass-through statics print
    /// the alias, a literal prints <c>Decimal</c>.
    /// </summary>
    [Test]
    public async Task UsingAlias_AndTheCanonicalName_BothStayBound()
    {
        var output = TranspileHelper.Transpile(
            """
            using Money = System.Decimal;

            [Transpile]
            public static class Prices
            {
                public static decimal Abs(decimal price) => Money.Abs(price);

                public static decimal Floor() => Money.Floor(2.7m);

                public static decimal Max(decimal price) => Money.Max(price, 3m);
            }
            """
        )["prices.ts"];

        await Assert
            .That(output)
            .Contains(
                "import { Decimal, Decimal as Money } from \"metano-runtime/system/decimal\";"
            );

        await Assert.That(output).Contains("Money.floor(new Decimal(\"2.7\"))");
    }

    /// <summary>
    /// The common shape: the canonical name only in signatures (type-only) and
    /// the alias only as a call's receiver (a value). Each binding keeps its own
    /// type-only flag, so the alias is a value binding.
    /// </summary>
    [Test]
    public async Task UsingAlias_AsValue_BesideATypeOnlyCanonical_IsAValueBinding()
    {
        var output = TranspileHelper.Transpile(
            """
            using Money = System.Decimal;

            [Transpile]
            public static class Prices
            {
                public static decimal Abs(decimal price) => Money.Abs(price);
            }
            """
        )["prices.ts"];

        await Assert
            .That(output)
            .Contains(
                "import { type Decimal, Decimal as Money } from \"metano-runtime/system/decimal\";"
            );
    }

    /// <summary>
    /// The inverse: a value canonical binding beside a type-only alias keeps
    /// the alias type-only.
    /// </summary>
    [Test]
    public async Task ImportBindings_KeepATypeOnlyAliasBesideAValueCanonical()
    {
        var bindings = new Metano.Compiler.TypeScript.Transformation.ImportBindings();

        bindings.Add("Decimal", alias: null, typeOnly: false);
        bindings.Add("Decimal", alias: "Money", typeOnly: true);

        var import = bindings.ToImport("decimal.js");

        await Assert.That(import.Names).IsEquivalentTo(["Decimal"]);
        await Assert.That(import.TypeOnly).IsFalse();

        await Assert
            .That(import.ExtraAliases!.Single())
            .IsEqualTo(
                new Metano.Compiler.TypeScript.AST.TsImportAlias("Decimal", "Money", TypeOnly: true)
            );
    }

    /// <summary>
    /// Two aliases of one BCL-origin type in a file: decimal.js's export is
    /// bound under each alias the source spells. Two aliases of a same-project
    /// type still bind only the printed one (a known gap).
    /// </summary>
    [Test]
    public async Task TwoAliasesOfOneType_BindEach()
    {
        var output = TranspileHelper.Transpile(
            """
            using Money = System.Decimal;
            using Cash = System.Decimal;

            [Transpile]
            public static class Prices
            {
                public static decimal Abs(decimal price) => Money.Abs(price);

                public static decimal Floor(decimal price) => Cash.Floor(price);
            }
            """
        )["prices.ts"];

        await Assert.That(output).Contains("Decimal as Money");
        await Assert.That(output).Contains("Decimal as Cash");
        await Assert.That(output).DoesNotContain("{ Money");
        await Assert.That(output).DoesNotContain(", Money,");
    }
}
