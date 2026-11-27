using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for the ambient contract carried by <c>[External]</c>:
/// <list type="bullet">
///   <item>Type with <c>[External]</c> generates NO .ts file</item>
///   <item>Other transpiled code can reference it (compiles in C#) but its name does
///   NOT appear as an import anywhere</item>
///   <item>When a lambda parameter's type is <c>[External]</c>, the lambda is emitted
///   without a parameter type annotation, letting TypeScript infer from context</item>
/// </list>
/// </summary>
public class ExternalAmbientTranspileTests
{
    private const string DomLibrary = """
        using Metano.Annotations;
        using Metano.Annotations.TypeScript;

        [assembly: TranspileAssembly]
        [assembly: EmitPackage("dom-lib")]

        namespace DomLib.Dom;

        [External]
        public interface HTMLElement
        {
            string Id { get; }
        }
        """;

    private const string AmbientLibrary = """
        using Metano.Annotations;
        using Metano.Annotations.TypeScript;

        [assembly: TranspileAssembly]
        [assembly: EmitPackage("dom-lib")]

        namespace DomLib.Dom;

        [External]
        public abstract class Window
        {
            public static Window Current { get; } = null!;

            public class Frame
            {
                public int Index { get; set; }
            }

            [StringEnum]
            public enum Kind
            {
                X,
                Y,
            }

            public interface IListener
            {
                void Handle();
            }
        }

        [External]
        public abstract class EventTarget
        {
            public string Name { get; set; } = "";
        }

        [External]
        public interface IDisposableNode
        {
            string Id { get; }
        }
        """;

    private const string AmbientConsumer = """
        using System;
        using DomLib.Dom;

        [Transpile]
        public class Probe : EventTarget, IDisposableNode
        {
            public string Id => Name;

            public Window Current() => Window.Current;

            public Type WindowType() => typeof(Window);

            public bool IsWindow(object o) => o is Window;

            public Window? AsWindow(object o) => o as Window;

            public Window.Frame? FrameOf(Window.Frame? frame) => frame;

            public Window.Kind? KindOf(Window.Kind? kind) => kind;

            public Window.Kind Why() => Window.Kind.Y;

            public bool IsKind(object o) => o is Window.Kind;

            public bool IsListener(object o) => o is Window.IListener;

            public int Measure(Func<Window.Frame, int> measure) => measure(new Window.Frame());

            public int Use() => Measure(f => f.Index);
        }
        """;

    [Test]
    public async Task ExternalType_DoesNotProduceFile()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [External]
            public interface IAmbient
            {
                IAmbient Text(string text);
            }

            [Transpile]
            public class Holder
            {
                public int Value { get; set; }
            }
            """
        );

        await Assert.That(result).DoesNotContainKey("i-ambient.ts");
        await Assert.That(result).ContainsKey("holder.ts");
    }

    [Test]
    public async Task ExternalType_NotImportedFromConsumer()
    {
        // Even when a transpiled type references an [Import]'d external class whose
        // method takes a callback over an [External] interface, the .ts output must
        // not try to import the [External] type from anywhere.
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations.TypeScript;

            [assembly: TranspileAssembly]

            [Import(name: "ExternalLib", from: "external-lib")]
            public class ExternalLib
            {
                public void Subscribe(Action<IExternalContext> handler) => throw new NotSupportedException();
            }

            [External]
            public interface IExternalContext
            {
                IExternalContext Send(string text);
            }

            public class Wiring
            {
                public void Setup()
                {
                    var ext = new ExternalLib();
                    ext.Subscribe(c => c.Send("hi"));
                }
            }
            """
        );

        var output = result["wiring.ts"];
        await Assert.That(output).Contains("import { ExternalLib } from \"external-lib\"");
        await Assert.That(output).DoesNotContain("IExternalContext");
    }

    [Test]
    public async Task ExternalLambdaParameter_OmitsTypeAnnotation()
    {
        // The lambda parameter c has C# type IExternalContext (which is [External]).
        // The generated arrow function should have no `: IExternalContext` annotation —
        // so TypeScript infers the type from the External.subscribe signature in the
        // real .d.ts of "external-lib".
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations.TypeScript;

            [assembly: TranspileAssembly]

            [Import(name: "ExternalLib", from: "external-lib")]
            public class ExternalLib
            {
                public void Subscribe(Action<IExternalContext> handler) => throw new NotSupportedException();
            }

            [External]
            public interface IExternalContext
            {
                IExternalContext Send(string text);
            }

            public class Wiring
            {
                public void Setup()
                {
                    var ext = new ExternalLib();
                    ext.Subscribe(c => c.Send("hi"));
                }
            }
            """
        );

        var output = result["wiring.ts"];
        await Assert.That(output).Contains("(c) => c.send(\"hi\")");
        await Assert.That(output).DoesNotContain("c: I");
    }

    [Test]
    public async Task ExternalType_BindingLib_WithoutTranspileAssembly_SurfacesNameOverride()
    {
        // DOM binding pattern: the binding library is a plain-C# project
        // with no `[assembly: TranspileAssembly]` and no
        // `[assembly: EmitPackage]`. Every type is individually marked
        // `[External, Name("…")]` so Metano knows about them via Roslyn
        // metadata alone. The untargeted `[Name]` override must still
        // surface at reference sites on the consumer side.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            [External, Name("HTMLElement")]
            public abstract class HtmlElement {}
            """,
            """
            [assembly: TranspileAssembly]

            public class Renderer
            {
                public HtmlElement? Target { get; set; }
            }
            """
        );

        var output = result["renderer.ts"];
        await Assert.That(output).Contains("HTMLElement");
        await Assert.That(output).DoesNotContain("HtmlElement");
    }

    [Test]
    public async Task ExternalType_CrossAssembly_SurfacesUntargetedNameOverride()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("dom-bindings")]

            [External, Name("HTMLElement")]
            public abstract class HtmlElement {}
            """,
            """
            [assembly: TranspileAssembly]

            public class Renderer
            {
                public HtmlElement? Target { get; set; }

                public void Attach(HtmlElement element) {}
            }
            """
        );

        var output = result["renderer.ts"];
        await Assert.That(output).Contains("HTMLElement");
        await Assert.That(output).DoesNotContain("HtmlElement");
    }

    [Test]
    public async Task ExternalType_WithUntargetedNameOverride_SurfacesOverrideAtReferenceSites()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            [assembly: TranspileAssembly]

            [External, Name("HTMLElement")]
            public abstract class HtmlElement {}

            public class Renderer
            {
                public HtmlElement? Target { get; set; }

                public void Attach(HtmlElement element) {}
            }
            """
        );

        var output = result["renderer.ts"];
        await Assert.That(output).Contains("HTMLElement");
        await Assert.That(output).DoesNotContain("HtmlElement");
    }

    [Test]
    public async Task ExternalTypeFromReferencedPackage_IsNotImported()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            DomLibrary,
            """
            using System.Collections.Generic;
            using DomLib.Dom;

            [Transpile]
            public class Renderer
            {
                public HTMLElement? Target { get; set; }

                private HTMLElement? _cached;

                public List<HTMLElement> Children { get; } = new();

                public string Describe(HTMLElement element)
                {
                    HTMLElement local = element;
                    _cached = local;
                    return local.Id;
                }
            }
            """
        );

        var output = result["renderer.ts"];
        await Assert.That(output).Contains("HTMLElement");
        await Assert.That(output).DoesNotContain("dom-lib");
    }

    [Test]
    public async Task NonExternalTypeFromReferencedPackage_StillImported()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("dom-lib")]

            namespace DomLib.Dom;

            public record Point(int X, int Y);
            """,
            """
            using DomLib.Dom;

            [Transpile]
            public class Renderer
            {
                public Point? Target { get; set; }
            }
            """
        );

        await Assert.That(result["renderer.ts"]).Contains("import");
        await Assert.That(result["renderer.ts"]).Contains("dom-lib");
    }

    [Test]
    public async Task ExternalTypesFromReferencedPackage_InValuePositionsAreNotImported()
    {
        var result = TranspileHelper.TranspileWithLibrary(AmbientLibrary, AmbientConsumer);

        var output = result["probe.ts"];
        await Assert.That(output).Contains("Window.current");
        await Assert.That(output).Contains("Window.Kind.Y");
        await Assert.That(output).Contains("return this.measure((f) => f.index);");
        await Assert.That(output).DoesNotContain("dom-lib");
    }

    [Test]
    public async Task ExternalTypesFromReferencedPackage_AddNoPackageDependency()
    {
        var library = TranspileHelper.CompileLibrary(AmbientLibrary);
        var consumer = TranspileHelper.CompileConsumer(AmbientConsumer, library);

        var transformer = TranspileHelper.NewTransformer(consumer);
        transformer.TransformAll();

        await Assert.That(transformer.CrossPackageDependencies.ContainsKey("dom-lib")).IsFalse();
    }

    [Test]
    public async Task IsOverInterfaceNestedInReferencedExternal_ReportsUnsupported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            AmbientLibrary,
            AmbientConsumer
        );

        await Assert.That(files["probe.ts"]).Contains("Unsupported expression: IsExpression");

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsTrue();
    }
}
