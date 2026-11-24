namespace Metano.Tests;

/// <summary>
/// Every <c>using</c> alias a file spells binds an import, whatever else is imported under the
/// type's name, and a template that names its receiver's type declares it (#675).
/// </summary>
public class UsingAliasBindingTests
{
    [Test]
    public async Task TwoAliasesOfASameProjectType_BindEach()
    {
        var output = TranspileHelper.Transpile(
            """
            using W1 = App.Widget;
            using W2 = App.Widget;

            namespace App
            {
                [Transpile]
                public class Widget
                {
                    public static Widget Make(int v) => new Widget();
                }

                [Transpile]
                public class Use
                {
                    public Widget A(int v) => W1.Make(v);

                    public Widget B(int v) => W2.Make(v);
                }
            }
            """
        )["app/use.ts"];

        await Assert.That(output).Contains("Widget as W1");
        await Assert.That(output).Contains("Widget as W2");
        await Assert.That(output).Contains("W1.make(v)");
        await Assert.That(output).Contains("W2.make(v)");
    }

    [Test]
    public async Task ImportAliasBesideAUsingAlias_BindsBoth()
    {
        var output = TranspileHelper.Transpile(
            """
            using W1 = App.Widget;

            namespace App
            {
                [ImportAlias(typeof(App.Widget), "Pinned")]
                file class TsModule;

                [Transpile]
                public class Widget
                {
                    public static Widget Make(int v) => new Widget();
                }

                [Transpile]
                public class Use
                {
                    public Widget A(int v) => W1.Make(v);
                }
            }
            """
        )["app/use.ts"];

        await Assert.That(output).Contains("Widget as Pinned");
        await Assert.That(output).Contains("Widget as W1");
        await Assert.That(output).Contains("W1.make(v)");
    }

    [Test]
    public async Task ImportAliasBesideACrossPackageUsingAlias_BindsBoth()
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

            [ImportAlias(typeof(Shared.Status), "Phase")]
            file class TsModule;

            public class Order
            {
                public State Current { get; set; } = State.Active;
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(library, consumer)["app/order.ts"];

        await Assert.That(output).Contains("Status as Phase");
        await Assert.That(output).Contains("Status as State");
        await Assert.That(output).Contains("State.Active");
    }

    [Test]
    public async Task NoContainerFactoryNamedLikeAnAliasedType_IsImportedToo()
    {
        var result = TranspileHelper.Transpile(
            """
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

                [Transpile]
                public class Page
                {
                    public ColumnWidget Build() => Ui.Column(5);
                }
            }
            """
        );

        var output = result["app/page.ts"];

        await Assert.That(output).Contains("import type { Column as ColumnWidget }");
        await Assert.That(output).Contains("import { Column } from \"./ui.js\"");
        await Assert.That(output).Contains("Column(5)");
    }

    [Test]
    public async Task AliasedCrossPackageEnum_WithASynthesizedDefault_ImportsTheCanonicalName()
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
                public State Current { get; set; }
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(library, consumer)["app/order.ts"];

        await Assert.That(output).Contains("Status.Active");

        await Assert
            .That(output)
            .Contains("import { Status, type Status as State } from \"shared/shared\";");
    }

    [Test]
    public async Task ExplicitTypeArgument_IsKeptOnTheCall()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Shape { }

            [Transpile]
            public class Circle : Shape { }

            [Transpile]
            public class Square : Shape { }

            [Transpile]
            public static class Guard
            {
                public static void ThrowIfEqual<T>(T first, T second) { }
            }

            [Transpile]
            public class Probe
            {
                public void Run(Circle circle, Square square) =>
                    Guard.ThrowIfEqual<Shape>(circle, square);
            }
            """
        )["probe.ts"];

        await Assert.That(output).Contains("Guard.throwIfEqual<Shape>(circle, square)");
    }

    /// <summary>
    /// A <c>null</c> literal passed where a written type argument is not
    /// nullable in TypeScript (nullable-oblivious code) leaves the type to
    /// inference, which accepts the call; a nullable type argument is kept.
    /// </summary>
    [Test]
    public async Task ExplicitTypeArgument_IsDroppedForANullLiteralOfANonNullableType()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Probe
            {
            #nullable disable
                public static string Oblivious() => Gen<string>(null);
            #nullable enable
                public static string Annotated() => Gen<string?>(null);

                static string Gen<T>(T x) => "g";
            }
            """
        )["probe.ts"];

        await Assert.That(output).Contains("return Probe.gen(null);");
        await Assert.That(output).Contains("return Probe.gen<string | null>(null);");
    }

    [Test]
    public async Task DecimalZero_ImportsDecimalWhenNoSignatureNamesIt()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Probe
            {
                public static void Run()
                {
                    System.Console.WriteLine(decimal.Zero.ToString());
                    System.Console.WriteLine(decimal.One.ToString());
                    System.Console.WriteLine(decimal.MinusOne.ToString());
                    System.Console.WriteLine(decimal.Parse("1.5").ToString());
                }
            }
            """
        )["probe.ts"];

        await Assert
            .That(output)
            .Contains("import { Decimal } from \"metano-runtime/system/decimal\";");

        await Assert.That(output).Contains("new Decimal(0)");
        await Assert.That(output).Contains("parseDecimal(\"1.5\", Decimal)");
    }

    [Test]
    public async Task EmitTemplateNamingItsOwnType_ThroughTypePlaceholder_ImportsIt()
    {
        var output = TranspileHelper.Transpile(
            """
            using F = App.Fmt;

            namespace App
            {
                [Transpile]
                public static class Fmt
                {
                    [Emit("$Type.make($0)")]
                    public static string Make(int v) => "";
                }

                [Transpile]
                public class Use
                {
                    public string A() => Fmt.Make(1);

                    public string B() => F.Make(2);
                }
            }
            """
        )["app/use.ts"];

        await Assert.That(output).Contains("Fmt.make(1)");
        await Assert.That(output).Contains("Fmt.make(2)");
        await Assert.That(output).Contains("Fmt");
        await Assert.That(output).Contains("from \"./fmt.js\"");
    }

    [Test]
    public async Task EmitTemplateNamingItsOwnType_FromAnotherPackage_ImportsIt()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shared")]

            namespace Shared;

            public static class Fmt
            {
                [Emit("$Type.make($0)")]
                public static string Make(int v) => "";
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Use
            {
                public string A() => Shared.Fmt.Make(1);
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(library, consumer)["app/use.ts"];

        await Assert.That(output).Contains("Fmt.make(1)");
        await Assert.That(output).Contains("import { Fmt } from \"shared/shared\";");
    }
}
