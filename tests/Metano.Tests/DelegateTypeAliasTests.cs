using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

public class DelegateTypeAliasTests
{
    [Test]
    public async Task TranspilableVoidDelegate_EmitsTypeAlias()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public delegate void Notifier(string message);
            """
        );

        var output = result["app/notifier.ts"];
        await Assert.That(output).Contains("export type Notifier = (message: string) => void;");
    }

    [Test]
    public async Task TranspilableReturningDelegate_EmitsTypeAlias()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public delegate int Folder(int acc, int next);
            """
        );

        var output = result["app/folder.ts"];
        await Assert
            .That(output)
            .Contains("export type Folder = (acc: number, next: number) => number;");
    }

    [Test]
    public async Task GenericDelegate_EmitsAliasWithTypeParameters()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public delegate TOut Converter<TIn, TOut>(TIn value);
            """
        );

        var output = result["app/converter.ts"];
        await Assert
            .That(output)
            .Contains("export type Converter<TIn, TOut> = (value: TIn) => TOut;");
    }

    [Test]
    public async Task GenericDelegate_WithConstraint_EmitsExtendsClause()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            namespace App;

            [Transpile]
            public interface ICloneable<T> {}

            [Transpile]
            public delegate T Cloner<T>(T value) where T : ICloneable<T>;
            """
        );

        var output = result["app/cloner.ts"];
        await Assert.That(output).Contains("export type Cloner<T extends ICloneable<T>>");
    }

    [Test]
    public async Task ConsumerProperty_UsesAliasNameInsteadOfInlining()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public delegate void ClickHandler();

            [Transpile]
            public sealed class Button
            {
                public ClickHandler? OnClick { get; set; }
            }
            """
        );

        var button = result["app/button.ts"];
        await Assert.That(button).Contains("import type { ClickHandler }");
        await Assert.That(button).Contains("onClick: ClickHandler | null = null");
    }

    [Test]
    public async Task AmbientDelegate_StillInlinesAtUsageSite()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            namespace App;

            [Transpile]
            public sealed class Button
            {
                public Action<string>? OnClick { get; set; }
            }
            """
        );

        var output = result["app/button.ts"];
        await Assert.That(output).Contains("(obj: string) => void");
    }

    [Test]
    public async Task AssemblyWideTranspile_PicksUpPublicDelegate()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            namespace App;

            public delegate void Listener(int value);

            public sealed class Source
            {
                public Listener? OnFire { get; set; }
            }
            """
        );

        await Assert.That(result.ContainsKey("app/listener.ts")).IsTrue();
        var alias = result["app/listener.ts"];
        await Assert.That(alias).Contains("export type Listener = (value: number) => void;");
        var source = result["app/source.ts"];
        await Assert.That(source).Contains("import type { Listener }");
    }

    [Test]
    public async Task DartTarget_TranspileDelegate_EmitsTypedef()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            namespace App;

            [Transpile]
            public delegate void Notifier(string message);
            """
        );

        await Assert.That(files).ContainsKey("notifier.dart");
        await Assert
            .That(files["notifier.dart"])
            .Contains("typedef Notifier = void Function(String);");
        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsFalse();
    }

    [Test]
    public async Task NestedDelegate_InsideTranspilableType_EmitsAlias()
    {
        // #153 gap: a `public delegate ...` declared inside another
        // [Transpile] class should still emit as a TS type alias so
        // consumers can reference `Outer.Inner`. The legacy path didn't
        // discover the nested declaration.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Outer
            {
                [Transpile]
                public delegate void Inner(string message);
            }
            """
        );

        var output = result["app/outer.ts"];
        await Assert.That(output).Contains("Inner");
        await Assert.That(output).Contains("(message: string) => void");
    }

    [Test]
    public async Task GenericDelegate_WithMultipleConstraints_EmitsIntersection()
    {
        // #153 gap: `where T : IFoo, IBar` should lower to
        // `T extends IFoo & IBar` instead of dropping every constraint
        // beyond the first. Aligns the delegate bridge with class /
        // interface bridges, all of which share the same mapper.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public interface IFoo {}

            [Transpile]
            public interface IBar {}

            [Transpile]
            public delegate T Combine<T>(T value) where T : IFoo, IBar;
            """
        );

        var output = result["app/combine.ts"];
        await Assert.That(output).Contains("T extends IFoo & IBar");
    }

    [Test]
    public async Task DelegateAlias_InsideGenericArgument_SubstitutesCorrectly()
    {
        // #153 gap: a [Transpile] delegate referenced as the generic
        // type argument of an Action / Func / List etc. must surface
        // by its alias name, not as the inlined function-type shape.
        var result = TranspileHelper.Transpile(
            """
            using System;

            namespace App;

            [Transpile]
            public delegate void Notifier(string message);

            [Transpile]
            public sealed class Hub
            {
                public Action<Notifier>? OnRegister { get; set; }
            }
            """
        );

        var output = result["app/hub.ts"];
        await Assert.That(output).Contains("(obj: Notifier) => void");
        await Assert.That(output).Contains("import type { Notifier }");
    }

    [Test]
    public async Task DelegateAlias_BarrelReExport_PreservesTypeQualifier()
    {
        // #153 gap: when a delegate alias re-exports through a directory
        // barrel, the `export type` qualifier must be preserved so
        // `tsc --isolatedModules` keeps the declaration as a type-only
        // re-export. With two top-level types under different namespaces
        // the package root sits one level above, forcing a sub-barrel
        // for the events folder.
        var result = TranspileHelper.Transpile(
            """
            namespace App.Events
            {
                [Transpile]
                public delegate void Handler(int code);
            }

            namespace App.Other
            {
                [Transpile]
                public sealed class Marker {}
            }
            """
        );

        await Assert.That(result).ContainsKey("app/events/index.ts");
        var barrel = result["app/events/index.ts"];
        await Assert.That(barrel).Contains("export type { Handler } from \"./handler\"");
    }

    [Test]
    public async Task CrossPackageDelegate_EmitsImportFromPackage()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace MyLib.Events
            {
                public delegate void Handler(int code);
            }

            namespace MyLib.Other
            {
                public record Marker(string Tag);
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Subscriber
            {
                public MyLib.Events.Handler? Callback { get; set; }
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);
        var output = result["app/subscriber.ts"];
        await Assert
            .That(output)
            .Contains("import type { Handler } from \"@scope/lib/my-lib/events\"");
        await Assert.That(output).Contains("callback: Handler | null = null");
    }
}
