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

        await Assert.That(output).Contains("import { Decimal } from \"decimal.js\";");
        await Assert.That(output).Contains("half(price: Decimal): Decimal");
        await Assert.That(output).Contains("return new Decimal(parseDecimalText(text));");
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
}
