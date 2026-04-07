namespace MetaSharp.Tests;

public class NamespaceTranspileTests
{
    [Test]
    public async Task SameNamespace_GeneratesFlatFiles()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile, StringEnum]
                public enum Status { Active, Inactive }

                [Transpile]
                public readonly record struct User(string Name, Status Status);
            }
            """
        );

        // Root namespace is App.Domain → both at root
        await Assert.That(result).ContainsKey("status.ts");
        await Assert.That(result).ContainsKey("user.ts");
    }

    [Test]
    public async Task DifferentNamespaces_GenerateSubFolders()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile, StringEnum]
                public enum Currency { Brl, Usd }
            }

            namespace App.Domain.Models
            {
                [Transpile]
                public readonly record struct Price(int Amount);
            }
            """
        );

        // Root namespace is App.Domain
        // Currency → root, Price → Models/
        await Assert.That(result).ContainsKey("currency.ts");
        await Assert.That(result).ContainsKey("models/price.ts");
    }

    [Test]
    public async Task CrossNamespaceImport_UsesRelativePath()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile, StringEnum]
                public enum Currency { Brl, Usd }
            }

            namespace App.Domain.Models
            {
                [Transpile]
                public readonly record struct Price(int Amount, App.Domain.Currency Currency);
            }
            """
        );

        var priceTs = result["models/price.ts"];
        // Import should go up one level to find Currency
        await Assert.That(priceTs).Contains("from \"../currency\"");
    }

    [Test]
    public async Task IndexFile_GeneratedPerDirectory()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile, StringEnum]
                public enum Currency { Brl, Usd }
            }

            namespace App.Domain.Models
            {
                [Transpile]
                public readonly record struct Price(int Amount);
            }
            """
        );

        // Root index should re-export Currency (StringEnum is a value, not type-only)
        await Assert.That(result).ContainsKey("index.ts");
        var rootIndex = result["index.ts"];
        await Assert.That(rootIndex).Contains("export { Currency } from \"./currency\"");

        // Models index should re-export Price
        await Assert.That(result).ContainsKey("models/index.ts");
        var modelsIndex = result["models/index.ts"];
        await Assert.That(modelsIndex).Contains("export { Price } from \"./price\"");
    }

    [Test]
    public async Task RootIndex_DoesNotReExportSubDirectories()
    {
        // Leaf-only barrels: parent index does NOT re-export subdirectories.
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile, StringEnum]
                public enum Status { Active }
            }

            namespace App.Domain.Models
            {
                [Transpile]
                public readonly record struct Item(string Name);
            }
            """
        );

        var rootIndex = result["index.ts"];
        await Assert.That(rootIndex).DoesNotContain("export * from \"./models\"");
    }

    [Test]
    public async Task SameNamespaceImport_UsesCurrentDirectory()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile, StringEnum]
                public enum Currency { Brl }

                [Transpile]
                public readonly record struct Money(int Cents, Currency Currency);
            }
            """
        );

        var moneyTs = result["money.ts"];
        await Assert.That(moneyTs).Contains("from \"./currency\"");
    }
}
