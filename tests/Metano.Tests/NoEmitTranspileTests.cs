namespace Metano.Tests;

/// <summary>
/// Tests for the <c>[NoEmit]</c> attribute. The contract:
/// <list type="bullet">
///   <item>Type with <c>[NoEmit]</c> generates NO .ts file</item>
///   <item>Other transpiled code can reference it (compiles in C#) but its name does
///   NOT appear as an import anywhere</item>
///   <item>When a lambda parameter's type is <c>[NoEmit]</c>, the lambda is emitted
///   without a parameter type annotation, letting TypeScript infer from context</item>
/// </list>
/// </summary>
public class NoEmitTranspileTests
{
    [Test]
    public async Task NoEmitType_DoesNotProduceFile()
    {
        var result = TranspileHelper.Transpile(
            """
            [NoEmit]
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
    public async Task NoEmitType_NotImportedFromConsumer()
    {
        // Even when a transpiled type references an [Import]'d external class whose
        // method takes a callback over a [NoEmit] interface, the .ts output must
        // not try to import the [NoEmit] type from anywhere.
        var result = TranspileHelper.Transpile(
            """
            using System;

            [assembly: TranspileAssembly]

            [Import(name: "External", from: "external-lib")]
            public class External
            {
                public void Subscribe(Action<IExternalContext> handler) => throw new NotSupportedException();
            }

            [NoEmit]
            public interface IExternalContext
            {
                IExternalContext Send(string text);
            }

            public class Wiring
            {
                public void Setup()
                {
                    var ext = new External();
                    ext.Subscribe(c => c.Send("hi"));
                }
            }
            """
        );

        var output = result["wiring.ts"];
        // The External class is imported (it has [Import]); the IExternalContext one is not.
        await Assert.That(output).Contains("import { External } from \"external-lib\"");
        await Assert.That(output).DoesNotContain("IExternalContext");
    }

    [Test]
    public async Task NoEmitLambdaParameter_OmitsTypeAnnotation()
    {
        // The lambda parameter c has C# type IExternalContext (which is [NoEmit]).
        // The generated arrow function should have no `: IExternalContext` annotation —
        // so TypeScript infers the type from the External.subscribe signature in the
        // real .d.ts of "external-lib".
        var result = TranspileHelper.Transpile(
            """
            using System;

            [assembly: TranspileAssembly]

            [Import(name: "External", from: "external-lib")]
            public class External
            {
                public void Subscribe(Action<IExternalContext> handler) => throw new NotSupportedException();
            }

            [NoEmit]
            public interface IExternalContext
            {
                IExternalContext Send(string text);
            }

            public class Wiring
            {
                public void Setup()
                {
                    var ext = new External();
                    ext.Subscribe(c => c.Send("hi"));
                }
            }
            """
        );

        var output = result["wiring.ts"];
        // Parameter `c` is bare (no type annotation), and the inner call is lowered.
        await Assert.That(output).Contains("(c) => c.send(\"hi\")");
        // Negative: no `c: ...` form anywhere.
        await Assert.That(output).DoesNotContain("c: I");
    }

    [Test]
    public async Task NoEmitType_BindingLib_WithoutTranspileAssembly_SurfacesNameOverride()
    {
        // DOM binding pattern on feat/jsx: the binding library is a
        // plain-C# project with no `[assembly: TranspileAssembly]` and
        // no `[assembly: EmitPackage]`. Every type is individually
        // marked `[NoEmit, Name("…")]` so Metano knows about them via
        // Roslyn metadata alone. The untargeted `[Name]` override must
        // still surface at reference sites on the consumer side.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            [NoEmit, Name("HTMLElement")]
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
    public async Task NoEmitType_CrossAssembly_SurfacesUntargetedNameOverride()
    {
        // Mirrors the DOM binding pattern on feat/jsx: `[NoEmit,
        // Name("HTMLElement")]` lives in a separate library; the
        // consumer references it as a parameter/property type. The
        // untargeted `[Name]` override must cross the assembly
        // boundary and surface at the reference site so the generated
        // TS interoperates with lib.dom.d.ts rather than using the C#
        // symbol name.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("dom-bindings")]

            [NoEmit, Name("HTMLElement")]
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
    public async Task NoEmitType_WithUntargetedNameOverride_SurfacesOverrideAtReferenceSites()
    {
        // A `[NoEmit, Name("HTMLElement")]` ambient stub (DOM binding
        // pattern) must surface the override at every type-reference
        // site. Untargeted `[Name]` — the single-arg overload — is
        // already picked up at extraction time via BuildQualifiedName;
        // this test pins the behavior so a later refactor doesn't
        // accidentally drop it for NoEmit types.
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            [NoEmit, Name("HTMLElement")]
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
}
