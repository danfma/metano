using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

public class ExternalMappingTranspileTests
{
    // ─── [ExportFromBcl] ────────────────────────────────────

    [Test]
    public async Task ExportFromBcl_OverridesHardcodedMapping()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: ExportFromBcl(typeof(decimal), FromPackage = "decimal.js", ExportedName = "Decimal")]

            [Transpile]
            public record Price(decimal Amount, string Currency);
            """
        );

        var output = result["price.ts"];
        await Assert.That(output).Contains("amount: Decimal");
        await Assert.That(output).Contains("from \"decimal.js\"");
    }

    [Test]
    public async Task Decimal_HasBuiltInMappingToDecimalJs()
    {
        // Metano ships a default [ExportFromBcl] for `decimal` in
        // Metano/Runtime/Decimal.cs, so user code that uses decimal automatically
        // imports `Decimal` from `decimal.js` without any per-project declaration.
        // The user is responsible for adding `decimal.js` to their package.json.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Price(decimal Amount, string Currency);
            """
        );

        var output = result["price.ts"];
        await Assert.That(output).Contains("amount: Decimal");
        await Assert.That(output).Contains("from \"decimal.js\"");
    }

    // ─── [Import] ───────────────────────────────────────────

    [Test]
    public async Task Import_TypeNotGenerated()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, Import("Decimal", from: "decimal.js")]
                public class ExternalDecimal { }

                [Transpile]
                public record Price(int Cents);
            }
            """
        );

        await Assert.That(result).DoesNotContainKey("app/external-decimal.ts");
        await Assert.That(result).ContainsKey("app/price.ts");
    }

    [Test]
    public async Task Import_ReferencedTypeGeneratesCorrectImport()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, Import("Moment", from: "moment")]
                public class Moment { }

                [Transpile]
                public record Event(string Name, Moment When);
            }
            """
        );

        var output = result["app/event.ts"];
        await Assert.That(output).Contains("from \"moment\"");
        await Assert.That(output).Contains("when: Moment");
    }

    // ─── [Emit] ─────────────────────────────────────────────

    [Test]
    public async Task Emit_MethodNotGeneratedInOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Helpers
            {
                [Emit("$0.toFixed($1)")]
                public static extern string ToFixed(decimal value, int digits);

                public static int Double(int x) => x * 2;
            }
            """
        );

        var output = result["helpers.ts"];
        await Assert.That(output).DoesNotContain("toFixed");
        await Assert.That(output).Contains("double");
    }

    [Test]
    public async Task Emit_InlinedAtCallSite()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Utils
            {
                [Emit("typeof $0")]
                public static extern string TypeOf(object value);

                public static string CheckType(object x)
                {
                    return TypeOf(x);
                }
            }
            """
        );

        var output = result["utils.ts"];
        await Assert.That(output).Contains("typeof x");
    }

    [Test]
    public async Task Emit_MultipleArguments()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Fmt
            {
                [Emit("$0.slice($1, $2)")]
                public static extern string Slice(string str, int start, int end);

                public static string Mid(string s)
                {
                    return Slice(s, 1, 3);
                }
            }
            """
        );

        var output = result["fmt.ts"];
        await Assert.That(output).Contains("s.slice(1, 3)");
    }

    [Test]
    public async Task Emit_TypeArgumentPlaceholderEmbedsTypeName()
    {
        // `$T0` substitutes the call-site's first generic type argument verbatim
        // into the lowered template — used to embed component class references
        // like `createElement(CounterApp, props)` without a typeof expression.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public class Widget { }

                [Transpile, NoContainer]
                public static class Factory
                {
                    [Emit("make($T0, $0)")]
                    public static extern object Of<T>(object props);
                }

                [Transpile]
                public static class Caller
                {
                    [ModuleEntryPoint]
                    public static void Run()
                    {
                        var x = Factory.Of<Widget>(new { });
                    }
                }
            }
            """
        );

        var caller = result["app/caller.ts"];
        await Assert.That(caller).Contains("make(Widget,");
        await Assert.That(caller).Contains("import { Widget }");
    }

    [Test]
    public async Task Emit_WithImport_DrivesConsumerImportLine()
    {
        // [Import] on the same method as [Emit] tells the consumer file which
        // identifier the template body references and where it lives. Without
        // this thread, the lowered call would name `createElement` but no
        // import line would resolve it.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, NoContainer]
                public static class H
                {
                    [Emit("createElement($T0, $0)"), Import(name: "createElement", from: "react")]
                    public static extern object Of<T>(object props);
                }

                [Transpile]
                public class Comp { }

                [Transpile]
                public static class Caller
                {
                    [ModuleEntryPoint]
                    public static void Run()
                    {
                        var x = H.Of<Comp>(new { });
                    }
                }
            }
            """
        );

        var caller = result["app/caller.ts"];
        await Assert.That(caller).Contains("createElement(Comp,");
        await Assert.That(caller).Contains("from \"react\"");
        await Assert.That(caller).Contains("createElement");
    }

    [Test]
    public async Task Emit_NamedArguments_ReorderToParameterDeclarationOrder()
    {
        // The [Emit] template author addresses arguments by index ($0, $1, …)
        // expecting parameter declaration order. Named arguments at the call
        // site must reorder before placeholder substitution; otherwise
        // `Slice(end: 3, start: 1)` would land $0=3, $1=1 and silently corrupt
        // the emitted call.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Fmt
            {
                [Emit("$0.slice($1, $2)")]
                public static extern string Slice(string str, int start, int end);

                public static string Mid(string s)
                {
                    return Slice(s, end: 3, start: 1);
                }
            }
            """
        );

        var output = result["fmt.ts"];
        await Assert.That(output).Contains("s.slice(1, 3)");
    }

    [Test]
    public async Task Emit_ParamsArray_SpreadsAtCallSite()
    {
        // `params T[]` at the [Emit] call site must flatten across positional
        // placeholder slots instead of landing as a single array.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Fmt
            {
                [Emit("join($0, $1, $2)")]
                public static extern string Join(string a, string b, string c);

                public static string Combine()
                {
                    return Join("x", "y", "z");
                }
            }
            """
        );

        var output = result["fmt.ts"];
        await Assert.That(output).Contains("join(\"x\", \"y\", \"z\")");
    }

    [Test]
    public async Task Emit_RefParameter_RaisesMs0023()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Bad
            {
                [Emit("inc($0)")]
                public static extern void Inc(ref int counter);
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidEmit)).IsTrue();
    }

    [Test]
    public async Task Emit_OutParameter_RaisesMs0023()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Bad
            {
                [Emit("compute($0)")]
                public static extern void Compute(out int result);
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidEmit)).IsTrue();
    }

    [Test]
    public async Task Emit_OmittedOptional_FillsExplicitDefault()
    {
        // When the caller skips an optional parameter, the declared default
        // must surface at the matching $N slot — otherwise the emitted call
        // would shift later args into the empty slot and miscount.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Fmt
            {
                [Emit("$0.slice($1, $2)")]
                public static extern string Slice(string str, int start = 0, int end = 10);

                public static string Head(string s)
                {
                    return Slice(s, end: 5);
                }
            }
            """
        );

        var output = result["fmt.ts"];
        await Assert.That(output).Contains("s.slice(0, 5)");
    }

    // ─── [Import] on methods/properties ─────────────────────

    [Test]
    public async Task Import_StaticMethod_NoStubEmitted()
    {
        // A method-level [Import] declares an external binding — the body is
        // unreachable noise. The declaring class should not emit a function
        // declaration for it.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, NoContainer]
                public static class Inferno
                {
                    [Import(name: "createElement", from: "inferno-create-element"), Name("createElement")]
                    public static object H(string tag, object props) => throw new System.NotSupportedException();
                }

                [Transpile]
                public static class Caller
                {
                    [ModuleEntryPoint]
                    public static void Run()
                    {
                        var x = Inferno.H("div", new { });
                    }
                }
            }
            """
        );

        // Inferno is fully erased — no inferno.ts file.
        await Assert.That(result).DoesNotContainKey("app/inferno.ts");
    }

    [Test]
    public async Task Import_StaticMethod_LowersToDirectCallAtCallSite()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, NoContainer]
                public static class Inferno
                {
                    [Import(name: "createElement", from: "inferno-create-element")]
                    public static object CreateElement(string tag, object props) =>
                        throw new System.NotSupportedException();
                }

                [Transpile]
                public static class Caller
                {
                    [ModuleEntryPoint]
                    public static void Run()
                    {
                        var x = Inferno.CreateElement("div", new { });
                    }
                }
            }
            """
        );

        var caller = result["app/caller.ts"];
        await Assert.That(caller).Contains("createElement(\"div\",");
        await Assert.That(caller).Contains("from \"inferno-create-element\"");
        await Assert.That(caller).DoesNotContain(".CreateElement(");
    }

    [Test]
    public async Task Import_CrossAssembly_StaticMethodWithDivergentName_PropagatesAlias()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("solid-bindings")]

            namespace App;

            [Transpile, NoContainer]
            public static class Solid
            {
                [Import(name: "createSignal", from: "solid-js")]
                public static object CreateRawSignal(object value) =>
                    throw new System.NotSupportedException();
            }
            """,
            """
            [assembly: TranspileAssembly]

            public class Caller
            {
                public object Use(int seed) => App.Solid.CreateRawSignal(seed);
            }
            """
        );

        var caller = result["caller.ts"];
        await Assert
            .That(caller)
            .Contains("import { createSignal as createRawSignal } from \"solid-js\"");
        await Assert.That(caller).Contains("createRawSignal(seed)");
    }

    [Test]
    public async Task Import_StaticMethodWithDivergentName_AliasesImportToMethodEmittedName()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, NoContainer]
                public static class Solid
                {
                    [Import(name: "createSignal", from: "solid-js")]
                    public static object CreateRawSignal(object value) =>
                        throw new System.NotSupportedException();
                }

                [Transpile]
                public static class Caller
                {
                    [ModuleEntryPoint]
                    public static void Run()
                    {
                        var x = Solid.CreateRawSignal(42);
                    }
                }
            }
            """
        );

        var caller = result["app/caller.ts"];
        await Assert
            .That(caller)
            .Contains("import { createSignal as createRawSignal } from \"solid-js\"");
        await Assert.That(caller).Contains("createRawSignal(42)");
        await Assert.That(caller).DoesNotContain("createSignal(42)");
    }

    [Test]
    public async Task Import_MethodOnTranspilableClass_NotEmittedAsClassMember()
    {
        // [Import] on an ordinary class member should not surface as a stub
        // method on the emitted class — the call site lowers separately.
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public class Wrapper
                {
                    [Import(name: "doIt", from: "lib")]
                    public static object Run(int n) => throw new System.NotSupportedException();
                }
            }
            """
        );

        var output = result["app/wrapper.ts"];
        // No function/method declaration for the [Import] member.
        await Assert.That(output).DoesNotContain("doIt(n: number)");
        await Assert.That(output).DoesNotContain("Run(n: number)");
    }
}
