namespace Metano.Tests;

/// <summary>
/// Lowering of C# interface inheritance (<c>interface IA : IB</c>) into
/// TypeScript <c>extends</c> clauses. Covers single + multiple bases,
/// generic bases (with BCL mapping), naming overrides, cross-package
/// bases, and <c>[PlainObject]</c> records that emit as interfaces.
/// </summary>
public class InterfaceInheritanceTests
{
    [Test]
    public async Task SingleBase_EmitsExtendsClause()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface ICloseable
            {
                void Close();
            }

            [Transpile]
            public interface IStream : ICloseable
            {
                int Read();
            }
            """
        );

        var output = result["i-stream.ts"];
        await Assert.That(output).Contains("export interface IStream extends ICloseable");
    }

    [Test]
    public async Task MultipleBases_EmitsCommaSeparatedExtends()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface ICloseable
            {
                void Close();
            }

            [Transpile]
            public interface IFlushable
            {
                void Flush();
            }

            [Transpile]
            public interface IStream : ICloseable, IFlushable
            {
                int Read();
            }
            """
        );

        var output = result["i-stream.ts"];
        await Assert.That(output).Contains("interface IStream extends ICloseable, IFlushable");
    }

    [Test]
    public async Task GenericBase_PropagatesTypeArguments()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IBox<T>
            {
                T Value { get; }
            }

            [Transpile]
            public interface ITypedBox<T> : IBox<T>
            {
                string Label { get; }
            }
            """
        );

        var output = result["i-typed-box.ts"];
        await Assert.That(output).Contains("interface ITypedBox<T> extends IBox<T>");
    }

    [Test]
    public async Task BclGenericBase_MapsThroughBclLayer()
    {
        // `IReadOnlyCollection<T>` is a BCL type that maps to TS
        // `Iterable<T>` via the existing IrToTsTypeMapper rules. The
        // extends clause must use the mapped name, not the C# original.
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public interface ITyped<T> : IReadOnlyCollection<T>
            {
                string Label { get; }
            }
            """
        );

        var output = result["i-typed.ts"];
        await Assert.That(output).Contains("interface ITyped<T> extends Iterable<T>");
        await Assert.That(output).DoesNotContain("IReadOnlyCollection");
    }

    [Test]
    public async Task NameOverrideOnBase_AppliesToExtendsEntry()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [Name("Closeable")]
            public interface ICloseable
            {
                void Close();
            }

            [Transpile]
            public interface IStream : ICloseable
            {
                int Read();
            }
            """
        );

        var output = result["i-stream.ts"];
        await Assert.That(output).Contains("interface IStream extends Closeable");
        await Assert.That(output).DoesNotContain("ICloseable");
    }

    [Test]
    public async Task StripInterfacePrefix_RewritesBothDeclarationAndExtends()
    {
        var (files, _) = TranspileHelper.TranspileWithStripInterfacePrefix(
            """
            [Transpile]
            public interface ICloseable
            {
                void Close();
            }

            [Transpile]
            public interface IStream : ICloseable
            {
                int Read();
            }
            """
        );

        var output = files["stream.ts"];
        await Assert.That(output).Contains("export interface Stream extends Closeable");
    }

    [Test]
    public async Task CrossPackageBase_AddsImportTypeEntry()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace MyLib.Streams
            {
                public interface ICloseable
                {
                    void Close();
                }
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public interface IStream : MyLib.Streams.ICloseable
            {
                int Read();
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);
        var output = result["i-stream.ts"];

        await Assert.That(output).Contains("import");
        await Assert.That(output).Contains("ICloseable");
        await Assert.That(output).Contains("@scope/lib");
        await Assert.That(output).Contains("interface IStream extends ICloseable");
    }

    [Test]
    public async Task NonTranspilableBase_FilteredFromExtends()
    {
        // A base interface that isn't transpiled (no [Transpile], no
        // declarative mapping) must not leak into the extends clause.
        // Mirrors the class implements filter to keep both shapes
        // consistent.
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public interface ICustom : IDisposable
            {
                void DoWork();
            }
            """
        );

        var output = result["i-custom.ts"];
        await Assert.That(output).Contains("interface ICustom");
        await Assert.That(output).DoesNotContain("extends IDisposable");
    }

    [Test]
    public async Task CrossPackageMemberType_OnInterface_AddsImport()
    {
        // Locks in the side-effect of wiring OriginResolver into the
        // interface extractor: cross-package types referenced by a
        // member (not just by a base) now resolve their origin and
        // surface as imports.
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace MyLib.Domain
            {
                public record Money(decimal Amount, string Currency);
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public interface IWallet
            {
                MyLib.Domain.Money Balance { get; }
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);
        var output = result["i-wallet.ts"];

        await Assert.That(output).Contains("import");
        await Assert.That(output).Contains("Money");
        await Assert.That(output).Contains("@scope/lib");
        await Assert.That(output).Contains("balance: Money");
    }

    [Test]
    public async Task PlainObjectRecord_HonorsBaseInterfaceChain()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IPet
            {
                string Name { get; }
            }

            [PlainObject]
            [Transpile]
            public record Cat(string Name) : IPet;
            """
        );

        var output = result["cat.ts"];
        await Assert.That(output).Contains("export interface Cat extends IPet");
    }
}
