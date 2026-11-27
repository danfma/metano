using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Self-typed statics of a CRTP base (spec 001 FR-113, #832): in an
/// <c>[EmitPackage]</c> library, where a public static cannot carry a hidden reified
/// parameter, a static whose only run-time need is <c>new TSelf()</c> (or forwarding
/// it) types its receiver (<c>this: new () =&gt; TSelf</c>) and constructs
/// <c>new this()</c>; every call is emitted on the class bound to the self parameter.
/// </summary>
public class CrtpStaticConstructionTests
{
    private const string Library = """
        using System;
        using Metano.Annotations;

        [assembly: TranspileAssembly]
        [assembly: EmitPackage("acme-lib")]

        namespace Lib;

        public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
        {
            public string Title { get; set; } = "";

            public static TSelf Use(Action<TSelf> build)
            {
                var c = new TSelf();
                build(c);
                return c;
            }

            public static TSelf Use2() => Use(c => { c.Title = "y"; });

            public static string Name() => "n";

            public static int Count()
            {
                var c = new TSelf();
                return c.Title.Length;
            }

            public static TSelf Twice()
            {
                var first = new TSelf();
                return Use(c => { c.Title = first.Title + "z"; });
            }
        }

        public class CardH : Component<CardH> { }

        public class Fancy : CardH { }

        public class Card<T> : Component<Card<T>>
        {
            public T? Value { get; set; }
        }
        """;

    private const string OverloadedLibrary = """
        using System;
        using Metano.Annotations;

        [assembly: TranspileAssembly]
        [assembly: EmitPackage("acme-lib")]

        namespace Lib;

        public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
        {
            public string Title { get; set; } = "";

            public static TSelf Use(Action<TSelf> build)
            {
                var c = new TSelf();
                build(c);
                return c;
            }

            public static TSelf Use(string title) => Use(c => { c.Title = title; });
        }

        public class CardH : Component<CardH> { }
        """;

    [Test]
    public async Task LibraryStatic_TypesItsReceiverAndConstructsIt()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Library);
        var component = files["lib/component.ts"];

        await Assert
            .That(component)
            .Contains(
                "static use<TSelf extends Component<TSelf>>(this: new () => TSelf, "
                    + "build: (obj: TSelf) => void): TSelf {"
            );

        await Assert.That(component).Contains("const c = new this();");
        await Assert.That(component).DoesNotContain("TSelf$type");

        await Assert
            .That(Codes(diagnostics))
            .DoesNotContain(DiagnosticCodes.UnreifiableTypeParameter);

        await Assert.That(Codes(diagnostics)).DoesNotContain(DiagnosticCodes.GenericNewConstraint);
    }

    [Test]
    public async Task LibraryStatic_ForwardingThroughItsReceiver_DeclaresTheStaticSide()
    {
        var component = TranspileHelper.Transpile(Library)["lib/component.ts"];

        await Assert
            .That(component)
            .Contains(
                "static use2<TSelf extends Component<TSelf>>(this: (new () => TSelf) & "
                    + "Omit<typeof Component, \"prototype\">): TSelf {"
            );

        await Assert.That(component).Contains("return this.use((c: TSelf) => {");
        await Assert.That(component).Contains("const first = new this();");
    }

    [Test]
    public async Task ConsumerCall_ThroughTheDerivedType_IsEmittedOnIt()
    {
        var program = ConsumerProgram("CardH.Use(c => { c.Title = \"x\"; })");

        await Assert.That(program).Contains("return CardH.use((c: CardH) => {");
    }

    [Test]
    public async Task ConsumerCall_ThroughTheBase_IsEmittedOnTheSelfArgument()
    {
        var program = ConsumerProgram("Component<CardH>.Use(c => { c.Title = \"x\"; })");

        await Assert.That(program).Contains("return CardH.use((c: CardH) => {");
        await Assert.That(program).DoesNotContain("Component");
    }

    [Test]
    public async Task ConsumerCall_ThroughAFurtherDerivedType_IsEmittedOnTheSelfArgument()
    {
        var program = ConsumerProgram("Fancy.Use(c => { c.Title = \"x\"; })");

        await Assert.That(program).Contains("return CardH.use((c: CardH) => {");
        await Assert.That(program).DoesNotContain("Fancy");
    }

    [Test]
    public async Task ConsumerCall_OfAForwardingStatic_IsEmittedOnTheSelfArgument()
    {
        var program = ConsumerProgram("Fancy.Use2()");

        await Assert.That(program).Contains("return CardH.use2();");
    }

    [Test]
    public async Task ConsumerCall_FromTheLibraryImage_DecidesFromTheSignature()
    {
        var files = TranspileHelper.TranspileWithLibraryImage(
            Library,
            Consumer("Fancy.Use(c => { c.Title = \"x\"; })"),
            referenceAssembly: true
        );

        await Assert.That(files["program.ts"]).Contains("return CardH.use((c: CardH) => {");
    }

    [Test]
    public async Task ConsumerCall_OnAGenericSelfArgument_PassesTheTypeArgument()
    {
        var program = ConsumerProgram("Component<Card<int>>.Use(c => { c.Value = 1; })");

        await Assert.That(program).Contains("return Card.use<Card<number>>((c: Card<number>) => {");
    }

    [Test]
    public async Task LibraryAndConsumer_TypeCheckStrictly()
    {
        var consumer = """
            using Lib;

            [Transpile]
            public static class Program
            {
                public static string Run() =>
                    CardH.Use(c => { c.Title = "a"; }).Title
                    + Component<CardH>.Use(c => { c.Title = "b"; }).Title
                    + Fancy.Use(c => { c.Title = "c"; }).Title
                    + CardH.Use2().Title
                    + Fancy.Twice().Title
                    + Component<Card<int>>.Use(c => { c.Value = 1; }).Title;
            }
            """;

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            TranspileHelper.Transpile(Library),
            TranspileHelper.TranspileWithLibrary(Library, consumer)
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task OverloadGroup_DispatchesThroughItsReceiver()
    {
        var component = TranspileHelper.Transpile(OverloadedLibrary)["lib/component.ts"];

        await Assert
            .That(component)
            .Contains(
                "static useBuild<TSelf extends Component<TSelf>>(this: new () => TSelf, "
                    + "build: (obj: TSelf) => void): TSelf {"
            );

        await Assert
            .That(component)
            .Contains(
                "static use<TSelf extends Component<TSelf>>(this: new () => TSelf, "
                    + "title: string): TSelf;"
            );

        await Assert
            .That(component)
            .Contains(
                "static use(this: (new () => any) & Omit<typeof Component, \"prototype\">, "
                    + "...args: unknown[]): any {"
            );

        await Assert.That(component).Contains("return this.useTitle(args[0] as any);");
    }

    [Test]
    public async Task OverloadGroup_ConsumerBindsTheFastPathOnTheSelfArgument()
    {
        var consumer = Consumer("Component<CardH>.Use(\"t\")");
        var library = TranspileHelper.Transpile(OverloadedLibrary);
        var files = TranspileHelper.TranspileWithLibrary(OverloadedLibrary, consumer);

        await Assert.That(files["program.ts"]).Contains("return CardH.useTitle(\"t\");");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(library, files);

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task TwoTypeParameterBase_DeclaresEveryClassTypeParameter()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Lib;

            public abstract class Widget<TSelf, TProps> where TSelf : Widget<TSelf, TProps>, new()
            {
                public static TSelf Make() => new TSelf();
            }

            public class Label : Widget<Label, string> { }

            public static class Screen
            {
                public static Label Show() => Widget<Label, string>.Make();
            }
            """
        );

        await Assert
            .That(files["lib/widget.ts"])
            .Contains(
                "static make<TSelf extends Widget<TSelf, TProps>, TProps>"
                    + "(this: new () => TSelf): TSelf {"
            );

        await Assert.That(files["lib/screen.ts"]).Contains("return Label.make();");
    }

    [Test]
    public async Task AppStatic_KeepsTheHiddenReifiedParameter()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;

            namespace App;

            [Transpile]
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public static TSelf Use(Action<TSelf> build)
                {
                    var c = new TSelf();
                    build(c);
                    return c;
                }
            }

            [Transpile]
            public sealed class CardH : Component<CardH> { }

            [Transpile]
            public static class Program
            {
                public static CardH Run() => Component<CardH>.Use(c => { });
            }
            """
        );

        await Assert
            .That(files["app/component.ts"])
            .Contains(
                "static use<TSelf extends Component<TSelf>>(build: (obj: TSelf) => void, "
                    + "TSelf$type: RuntimeType<TSelf>): TSelf {"
            );

        await Assert.That(files["app/component.ts"]).Contains("createInstance(TSelf$type)");
        await Assert.That(files["app/program.ts"]).Contains("Component.use((c: CardH) => {");
        await Assert.That(files["app/program.ts"]).Contains("}, CardH.$runtimeType);");
    }

    [Test]
    public async Task NonCrtpFactory_KeepsMs0054()
    {
        var messages = LibraryMessages(
            """
            public static class Factory<T> where T : new()
            {
                public static T Make() => new T();
            }
            """
        );

        await Assert
            .That(messages)
            .Contains(
                "Public member 'Factory.Make' in an [EmitPackage] library cannot carry a hidden "
                    + "reified type parameter in 3.0; make it internal/private or pass an explicit "
                    + "descriptor."
            );
    }

    [Test]
    public async Task TypeOfTheSelfParameter_KeepsMs0054()
    {
        var messages = LibraryMessages(
            """
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public static Type Kind() => typeof(TSelf);
            }
            """
        );

        await Assert
            .That(messages)
            .Contains(
                "Public member 'Component.Kind' in an [EmitPackage] library cannot carry a hidden "
                    + "reified type parameter in 3.0; make it internal/private or pass an explicit "
                    + "descriptor."
            );
    }

    [Test]
    public async Task CallFromAnInstanceMember_ReportsMs0054()
    {
        var messages = LibraryMessages(
            """
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public static TSelf Use(Action<TSelf> build)
                {
                    var c = new TSelf();
                    build(c);
                    return c;
                }

                public TSelf Again() => Use(c => { });
            }
            """
        );

        await Assert.That(messages).Contains(OpenSelfArgument("TSelf"));
    }

    [Test]
    public async Task OpenSelfArgumentInTheLibrary_ReportsMs0054()
    {
        var messages = LibraryMessages(
            """
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public static TSelf Use(Action<TSelf> build)
                {
                    var c = new TSelf();
                    build(c);
                    return c;
                }
            }

            public static class Screen
            {
                public static T Make<T>() where T : Component<T>, new() => Component<T>.Use(c => { });
            }
            """
        );

        await Assert.That(messages).Contains(OpenSelfArgument("T"));
    }

    [Test]
    public async Task ReferencedStatic_WithAnOpenSelfArgument_KeepsTheReceiverAndWarns()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            Library,
            """
            using Lib;

            [Transpile]
            public static class Program
            {
                public static string Name<T>() where T : Component<T>, new() => Component<T>.Name();
            }
            """
        );

        await Assert.That(files["program.ts"]).Contains("return Component.name();");

        await Assert
            .That(Warnings(diagnostics))
            .IsEquivalentTo([
                ReferencedWarning(
                    "Component.Name",
                    "the call binds the self type to the type parameter 'T', which names no "
                        + "class"
                ),
            ]);
    }

    [Test]
    public async Task ReferencedStatic_HiddenOnTheSelfArgument_KeepsTheReceiverAndWarns()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            Library,
            """
            using Lib;

            [Transpile]
            public class Shadow : Component<Shadow>
            {
                public static string Name(int x) => "shadow";
            }

            [Transpile]
            public static class Program
            {
                public static string Run() => Component<Shadow>.Name();
            }
            """
        );

        await Assert.That(files["program.ts"]).Contains("return Component.name();");

        await Assert
            .That(Warnings(diagnostics))
            .IsEquivalentTo([
                "Static 'Shadow.Name' takes the name of 'Component.Name', declared in a "
                    + "referenced package: if 'Component.Name' builds the self type from the "
                    + "receiving class (spec 001 FR-113), a call on 'Shadow' reaches this "
                    + "static instead; rename it.",
                ReferencedWarning(
                    "Component.Name",
                    "'Shadow' or a class between it and 'Component' declares a static that "
                        + "takes the name 'Name'"
                ),
            ]);
    }

    [Test]
    public async Task ReferencedStatic_OnASelfClassWithReifiedSlots_KeepsTheReceiverAndWarns()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            Library,
            """
            using Lib;

            [Transpile]
            public class Slotted<T> : Component<Slotted<T>>
            {
                public bool Holds(object value) => value is T;
            }

            [Transpile]
            public static class Program
            {
                public static string Run() => Component<Slotted<int>>.Name();
            }
            """
        );

        await Assert.That(files["program.ts"]).Contains("return Component.name();");

        await Assert
            .That(Warnings(diagnostics))
            .IsEquivalentTo([
                ReferencedWarning(
                    "Component.Name",
                    "'Slotted<int>' carries reified type arguments that construction from the "
                        + "receiving class does not pass"
                ),
            ]);
    }

    [Test]
    public async Task ReferencedStatic_OfABaseWithoutConstructibleSelf_StaysSilent()
    {
        var library = """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Lib;

            public abstract class Node<TSelf> where TSelf : Node<TSelf>
            {
                public static string Name() => "n";
            }
            """;

        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            library,
            """
            using Lib;

            [Transpile]
            public static class Program
            {
                public static string Name<T>() where T : Node<T> => Node<T>.Name();
            }
            """
        );

        await Assert.That(files["program.ts"]).Contains("return Node.name();");
        await Assert.That(Messages(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task ReferencedSelfTypedStatic_WithAnOpenSelfOnAnAbstractBase_FailsStrictly()
    {
        var consumer = """
            using Lib;

            [Transpile]
            public static class Program
            {
                public static T Make<T>() where T : Component<T>, new() => Component<T>.Use(c => { });
            }
            """;

        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            Library,
            consumer
        );

        await Assert.That(files["program.ts"]).Contains("return Component.use((c: T) => {");

        await Assert
            .That(Warnings(diagnostics))
            .IsEquivalentTo([
                ReferencedWarning(
                    "Component.Use",
                    "the call binds the self type to the type parameter 'T', which names no "
                        + "class"
                ),
            ]);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            TranspileHelper.Transpile(Library),
            files
        );

        await Assert.That(output).Contains("error TS2684");
        await Assert.That(exitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task ReferencedSelfTypedStatic_WithAnOpenSelfOnAConcreteBase_WarnsAndTypeChecks()
    {
        var library = """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Lib;

            public class Widget<TSelf> where TSelf : Widget<TSelf>, new()
            {
                public virtual string Kind => "widget";

                public static TSelf Use() => new TSelf();
            }

            public class Button : Widget<Button>
            {
                public override string Kind => "button";
            }
            """;

        var consumer = """
            using Lib;

            [Transpile]
            public static class Program
            {
                public static string MakeW<T>() where T : Widget<T>, new() => Widget<T>.Use().Kind;

                public static string Run() => MakeW<Button>();
            }
            """;

        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            library,
            consumer
        );

        await Assert.That(files["program.ts"]).Contains("return Widget.use().kind;");

        await Assert
            .That(Warnings(diagnostics))
            .IsEquivalentTo([
                ReferencedWarning(
                    "Widget.Use",
                    "the call binds the self type to the type parameter 'T', which names no "
                        + "class"
                ),
            ]);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            TranspileHelper.Transpile(library),
            files
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task HidingStaticByNameAttribute_ReportsMs0054AtItsDeclaration()
    {
        var messages = LibraryMessages(
            """
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public static TSelf Use() => new TSelf();
            }

            public class CardH : Component<CardH>
            {
                [Name("use")]
                public static string Other() => "other";
            }
            """
        );

        await Assert
            .That(messages)
            .Contains(
                "Static 'CardH.Other' takes the name of 'Component.Use', which builds the self "
                    + "type from the receiving class (spec 001 FR-113): a call on 'CardH' "
                    + "reaches this static instead; rename it."
            );
    }

    [Test]
    public async Task RenamedStaticOnTheSelfClass_DoesNotHideAndTypeChecksStrictly()
    {
        var library = """
            using System;
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Lib;

            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public string Title { get; set; } = "";

                public static TSelf Use(Action<TSelf> build)
                {
                    var c = new TSelf();
                    build(c);
                    return c;
                }
            }

            public class Renamed2 : Component<Renamed2>
            {
                [Name("build")]
                public static string Use(int n) => "n" + n;
            }

            public static class Screen
            {
                public static Renamed2 Show() => Component<Renamed2>.Use(c => { c.Title = "r"; });
            }
            """;

        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(library);

        await Assert.That(Messages(diagnostics)).IsEmpty();
        await Assert.That(files["lib/renamed2.ts"]).Contains("static build(n: number): string {");

        await Assert
            .That(files["lib/screen.ts"])
            .Contains("return Renamed2.use((c: Renamed2) => {");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task ConsumerStaticHidingAReferencedCrtpStatic_WarnsAtItsDeclaration()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            Library,
            """
            using Lib;

            [Transpile]
            public class Mine : Component<Mine>
            {
                [Name("use")]
                public static string Mine2() => "mine";
            }
            """
        );

        await Assert
            .That(Warnings(diagnostics))
            .IsEquivalentTo([
                "Static 'Mine.Mine2' takes the name of 'Component.Use', declared in a "
                    + "referenced package: if 'Component.Use' builds the self type from the "
                    + "receiving class (spec 001 FR-113), a call on 'Mine' reaches this "
                    + "static instead; rename it.",
            ]);
    }

    [Test]
    public async Task HidingStaticOfARenamedSelfTypedStatic_ReportsMs0054AtItsDeclaration()
    {
        var messages = LibraryMessages(
            """
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                [Name("create")]
                public static TSelf Use() => new TSelf();
            }

            public class CardH : Component<CardH>
            {
                public static string Create() => "create";
            }
            """
        );

        await Assert
            .That(messages)
            .Contains(
                "Static 'CardH.Create' takes the name of 'Component.Use', which builds the self "
                    + "type from the receiving class (spec 001 FR-113): a call on 'CardH' "
                    + "reaches this static instead; rename it."
            );
    }

    [Test]
    public async Task ReferencedNonSelfTypedStatic_OnATwoParameterBase_PassesTheDeclaredTypeArguments()
    {
        var library = """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Lib;

            public abstract class Widget<TSelf, TProps> where TSelf : Widget<TSelf, TProps>
            {
                public static TSelf Make(TSelf x) => x;
            }

            public class Box<T> : Widget<Box<T>, string> { }
            """;

        var consumer = Consumer("Widget<Box<int>, string>.Make(new Box<int>())");
        var libraryFiles = TranspileHelper.Transpile(library);
        var files = TranspileHelper.TranspileWithLibrary(library, consumer);

        await Assert
            .That(libraryFiles["lib/widget.ts"])
            .Contains(
                "static make<TSelf extends Widget<TSelf, TProps>, TProps>(x: TSelf): TSelf {"
            );

        await Assert
            .That(files["program.ts"])
            .Contains("return Box.make<Box<number>, string>(new Box<number>());");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(libraryFiles, files);

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task HidingStaticInTheLibrary_ReportsMs0054AtItsDeclarationAndCalls()
    {
        var messages = LibraryMessages(
            """
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public static TSelf Use(Action<TSelf> build)
                {
                    var c = new TSelf();
                    build(c);
                    return c;
                }
            }

            public class CardH : Component<CardH>
            {
                public static string Use(int n) => "n" + n;
            }

            public static class Screen
            {
                public static CardH Show() => Component<CardH>.Use(c => { });
            }
            """
        );

        await Assert
            .That(messages)
            .Contains(
                "Static 'CardH.Use' takes the name of 'Component.Use', which builds the self type "
                    + "from the receiving class (spec 001 FR-113): a call on 'CardH' reaches this "
                    + "static instead; rename it."
            );

        await Assert
            .That(messages)
            .Contains(
                SelfTyped(
                    "'CardH' or a class between it and 'Component' declares a static that takes "
                        + "the name 'Use'"
                )
            );
    }

    [Test]
    public async Task MixedStaticAndInstanceGroup_SplitsTheSidesAndTypeChecksStrictly()
    {
        var library = """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Lib;

            public abstract class Both<TSelf> where TSelf : Both<TSelf>, new()
            {
                public static TSelf Make() => new TSelf();

                public string Make(int n) => "i" + n;
            }

            public class BothX : Both<BothX> { }
            """;

        var libraryFiles = TranspileHelper.Transpile(library);
        var both = libraryFiles["lib/both.ts"];

        await Assert
            .That(both)
            .Contains(
                "static makeDefault<TSelf extends Both<TSelf>>(this: new () => TSelf): TSelf {"
            );

        await Assert
            .That(both)
            .Contains(
                "static make<TSelf extends Both<TSelf>>(this: (new () => TSelf) & "
                    + "Omit<typeof Both, \"prototype\">): TSelf {"
            );

        await Assert.That(both).Contains("return this.makeDefault();");

        await Assert.That(both).Contains("make(n: number): string {");

        var consumer = TranspileHelper.TranspileWithLibrary(
            library,
            Consumer("BothX.Make().Make(1)")
        );

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(libraryFiles, consumer);

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task ForwardingThroughAnIntermediateClass_ReachesTheBaseThroughItsReceiver()
    {
        var library = """
            using System;
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Lib;

            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public string Title { get; set; } = "";

                public static TSelf Use(Action<TSelf> build)
                {
                    var c = new TSelf();
                    build(c);
                    return c;
                }
            }

            public abstract class Mid<TSelf> : Component<TSelf> where TSelf : Mid<TSelf>, new()
            {
                public static TSelf Make() => Use(c => { c.Title = "mid"; });
            }

            public class Leaf : Mid<Leaf> { }
            """;

        var (libraryFiles, diagnostics) = TranspileHelper.TranspileWithDiagnostics(library);

        await Assert
            .That(libraryFiles["lib/mid.ts"])
            .Contains(
                "static make<TSelf extends Mid<TSelf>>(this: (new () => TSelf) & "
                    + "Omit<typeof Mid, \"prototype\">): TSelf {"
            );

        await Assert.That(libraryFiles["lib/mid.ts"]).Contains("return this.use((c: TSelf) => {");
        await Assert.That(Messages(diagnostics)).IsEmpty();

        var consumer = TranspileHelper.TranspileWithLibrary(
            library,
            Consumer("Leaf.Make().Title + Leaf.Use(c => { }).Title")
        );

        await Assert.That(consumer["program.ts"]).Contains("Leaf.make()");
        await Assert.That(consumer["program.ts"]).Contains("Leaf.use((c: Leaf) => {");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(libraryFiles, consumer);

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task LibraryConsumingALibrary_EmitsItsCallsOnTheSelfClass()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            Library,
            """
            using Metano.Annotations;
            using Lib;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-shelf")]

            namespace Shelf;

            public class Badge : Component<Badge> { }

            public static class Rack
            {
                public static Badge Make() => Component<Badge>.Use(b => { b.Title = "b"; });

                public static Badge Again() => Badge.Use2();
            }
            """
        );

        await Assert.That(files["shelf/rack.ts"]).Contains("return Badge.use((b: Badge) => {");
        await Assert.That(files["shelf/rack.ts"]).Contains("return Badge.use2();");
        await Assert.That(Messages(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task GenericSelfClass_TypeChecksStrictly()
    {
        var consumer = Consumer(
            "Component<Card<int>>.Use(c => { c.Value = 1; }).Value + Card<string>.Use2().Title "
                + "+ Component<Card<bool>>.Twice().Title + Component<Card<long>>.Count()"
        );

        var files = TranspileHelper.TranspileWithLibrary(Library, consumer);

        await Assert.That(files["program.ts"]).Contains("Card.use2<Card<string>>()");
        await Assert.That(files["program.ts"]).Contains("Card.twice<Card<boolean>>()");
        await Assert.That(files["program.ts"]).Contains("Card.count()");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            TranspileHelper.Transpile(Library),
            files
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task MethodGroupOfASelfTypedStatic_ReportsMs0054()
    {
        var messages = LibraryMessages(
            """
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public static TSelf Use(Action<TSelf> build)
                {
                    var c = new TSelf();
                    build(c);
                    return c;
                }
            }

            public class CardH : Component<CardH> { }

            public static class Screen
            {
                public static Func<Action<CardH>, CardH> Group() => CardH.Use;
            }
            """
        );

        await Assert
            .That(messages)
            .Contains(
                SelfTyped(
                    "a method-group conversion names no receiving class; use a lambda that calls "
                        + "it on a concrete class"
                )
            );
    }

    [Test]
    public async Task GenericSelfArgumentWithReifiedSlots_ReportsMs0054()
    {
        var messages = LibraryMessages(
            """
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public static TSelf Use(Action<TSelf> build)
                {
                    var c = new TSelf();
                    build(c);
                    return c;
                }
            }

            public class Card<T> : Component<Card<T>>
            {
                public bool Holds(object value) => value is T;
            }

            public static class Screen
            {
                public static Card<int> Show() => Component<Card<int>>.Use(c => { });
            }
            """
        );

        await Assert
            .That(messages)
            .Contains(
                SelfTyped(
                    "'Lib.Card<int>' carries reified type arguments that construction from the "
                        + "receiving class does not pass"
                )
            );
    }

    [Test]
    public async Task ConstructionInALocalFunction_KeepsMs0054()
    {
        var messages = LibraryMessages(
            """
            public abstract class Component<TSelf> where TSelf : Component<TSelf>, new()
            {
                public static TSelf Local()
                {
                    TSelf Make() => new TSelf();
                    return Make();
                }
            }
            """
        );

        await Assert
            .That(messages)
            .Contains(
                "Public member 'Component.Local' in an [EmitPackage] library cannot carry a hidden "
                    + "reified type parameter in 3.0; make it internal/private or pass an explicit "
                    + "descriptor."
            );
    }

    [Test]
    public async Task Dart_KeepsMs0019()
    {
        var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            Library,
            new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview
            )
        );

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "DartCrtpTest",
            [syntaxTree],
            TranspileHelper.BaseReferences,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary
            )
        );

        var ir = new Metano.Compiler.CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            Metano.Annotations.TargetLanguage.Dart
        );

        await Assert.That(Codes(ir.Diagnostics)).Contains(DiagnosticCodes.GenericNewConstraint);
    }

    private static string ConsumerProgram(string expression) =>
        TranspileHelper.TranspileWithLibrary(Library, Consumer(expression))["program.ts"];

    private static string Consumer(string expression) =>
        $$"""
            using Lib;

            [Transpile]
            public static class Program
            {
                public static object Run() => {{expression}};
            }
            """;

    private static List<string> LibraryMessages(string declarations) =>
        Messages(
            TranspileHelper
                .TranspileWithDiagnostics(
                    """
                    using System;
                    using Metano.Annotations;

                    [assembly: TranspileAssembly]
                    [assembly: EmitPackage("acme-lib")]

                    namespace Lib;

                    """ + declarations
                )
                .Diagnostics
        );

    private static string OpenSelfArgument(string typeParameter) =>
        SelfTyped(
            $"the call binds the self type to the type parameter '{typeParameter}', which names "
                + "no class"
        );

    private static string SelfTyped(string reason) =>
        "'Component.Use' builds the self type from the receiving class (spec 001 FR-113), but "
        + $"{reason}.";

    private static string ReferencedWarning(string member, string reason) =>
        $"'{member}' is declared in a referenced package and {reason}: if '{member}' builds the "
        + "self type from the receiving class (spec 001 FR-113), this call constructs the base "
        + "class at run time; call it on a concrete class.";

    private static List<string> Messages(IEnumerable<MetanoDiagnostic> diagnostics) =>
        [
            .. diagnostics
                .Where(d => d.Code == DiagnosticCodes.UnreifiableTypeParameter)
                .Select(d => d.Message),
        ];

    private static List<string> Warnings(IEnumerable<MetanoDiagnostic> diagnostics) =>
        [
            .. diagnostics
                .Where(d =>
                    d
                        is {
                            Code: DiagnosticCodes.UnreifiableTypeParameter,
                            Severity: MetanoDiagnosticSeverity.Warning,
                        }
                )
                .Select(d => d.Message),
        ];

    private static List<string> Codes(IEnumerable<MetanoDiagnostic> diagnostics) =>
        [.. diagnostics.Select(d => d.Code)];
}
