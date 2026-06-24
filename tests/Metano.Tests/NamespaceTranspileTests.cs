namespace Metano.Tests;

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

        // Full-namespace layout: App.Domain → app/domain/.
        await Assert.That(result).ContainsKey("app/domain/status.ts");
        await Assert.That(result).ContainsKey("app/domain/user.ts");
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

        // Full-namespace layout: App.Domain → app/domain/,
        // App.Domain.Models → app/domain/models/.
        await Assert.That(result).ContainsKey("app/domain/currency.ts");
        await Assert.That(result).ContainsKey("app/domain/models/price.ts");
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

        var priceTs = result["app/domain/models/price.ts"];
        // Different namespace → import the file directly via the alias.
        await Assert.That(priceTs).Contains("from \"#/app/domain/currency\"");
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

        // app/domain leaf barrel re-exports Currency (StringEnum is a value, not type-only)
        await Assert.That(result).ContainsKey("app/domain/index.ts");
        var domainIndex = result["app/domain/index.ts"];
        await Assert.That(domainIndex).Contains("export { Currency } from \"./currency\"");

        // app/domain/models leaf barrel re-exports Price
        await Assert.That(result).ContainsKey("app/domain/models/index.ts");
        var modelsIndex = result["app/domain/models/index.ts"];
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

        var domainIndex = result["app/domain/index.ts"];
        await Assert.That(domainIndex).DoesNotContain("export * from \"./models\"");
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

        var moneyTs = result["app/domain/money.ts"];
        // Same namespace uses relative file import to avoid a cycle through
        // the namespace barrel (`money.ts -> barrel -> ./money.ts`).
        await Assert.That(moneyTs).Contains("from \"./currency\"");
    }

    [Test]
    public async Task MultipleTypesFromSameNamespace_EachImportsFromOwnFile()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile]
                public class Priority { }

                [Transpile]
                public class Status { }

                [Transpile]
                public class Category { }
            }

            namespace App.Application
            {
                [Transpile]
                public sealed class Ticket
                {
                    public Ticket()
                    {
                        Source = new App.Domain.Priority();
                        Current = new App.Domain.Status();
                        Kind = new App.Domain.Category();
                    }

                    public App.Domain.Priority Source { get; }
                    public App.Domain.Status Current { get; }
                    public App.Domain.Category Kind { get; }
                }
            }
            """
        );

        var ticketTs = result["app/application/ticket.ts"];
        // Full-namespace layout: cross-namespace imports target each file
        // directly via the alias rather than merging through the barrel.
        await Assert.That(ticketTs).Contains("import { Category } from \"#/app/domain/category\";");
        await Assert.That(ticketTs).Contains("import { Priority } from \"#/app/domain/priority\";");
        await Assert.That(ticketTs).Contains("import { Status } from \"#/app/domain/status\";");
    }

    [Test]
    public async Task AllTypeOnlyFromSameNamespace_UsesWholeStatementImportType()
    {
        // Each type-only name imports from its own file via the alias, and a
        // single-name bucket prefers the whole-statement `import type { … }`
        // form over per-name `{ type A }` — the latter triggers Biome's
        // noImportTypeQualifier warning.
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile]
                public interface IReadable { string Read(); }

                [Transpile]
                public interface IWritable { void Write(string value); }
            }

            namespace App.Application
            {
                [Transpile]
                public interface IHandler
                {
                    void Handle(App.Domain.IReadable reader, App.Domain.IWritable writer);
                }
            }
            """
        );

        var handlerTs = result["app/application/i-handler.ts"];
        await Assert
            .That(handlerTs)
            .Contains("import type { IReadable } from \"#/app/domain/i-readable\";");
        await Assert
            .That(handlerTs)
            .Contains("import type { IWritable } from \"#/app/domain/i-writable\";");
        // No per-name type qualifier form.
        await Assert.That(handlerTs).DoesNotContain("{ type IReadable");
        await Assert.That(handlerTs).DoesNotContain("{ type IWritable");
    }

    [Test]
    public async Task MixedValueAndTypeOnlyFromSameNamespace_EachImportsFromOwnFile()
    {
        // Full-namespace layout: each name imports from its own file, so the
        // value stays a plain import and the type uses the whole-statement
        // `import type` form.
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile, StringEnum]
                public enum Priority { Low, High }

                [Transpile]
                public interface IReadable { string Read(); }
            }

            namespace App.Application
            {
                [Transpile]
                public sealed class Job
                {
                    public Job(App.Domain.IReadable reader)
                    {
                        Reader = reader;
                        Current = App.Domain.Priority.Low;
                    }

                    public App.Domain.Priority Current { get; }
                    public App.Domain.IReadable Reader { get; }
                }
            }
            """
        );

        var jobTs = result["app/application/job.ts"];
        await Assert
            .That(jobTs)
            .Contains("import type { IReadable } from \"#/app/domain/i-readable\";");
        await Assert.That(jobTs).Contains("import { Priority } from \"#/app/domain/priority\";");
    }
}
