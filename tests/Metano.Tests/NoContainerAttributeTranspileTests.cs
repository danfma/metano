using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for <c>[NoContainer]</c> from <c>Metano.Annotations</c>. The
/// attribute marks a static class whose scope vanishes at the call
/// site: static member access flattens to a bare identifier and the
/// class's members project as top-level exports in a file named
/// after the class (no TypeScript class wrapper). Covers the
/// flatten, the module-style emission, and the MS0015 validation
/// surface.
/// </summary>
public class NoContainerAttributeTranspileTests
{
    // ─── Emission flatten ────────────────────────────────────

    [Test]
    public async Task NoContainer_StaticMember_AccessFlattensToIdentifier()
    {
        // `Constants.Pi` on the C# side should lower to a bare `pi`
        // identifier on the TS side (camelCased per the TS naming
        // policy) because `Constants` is `[NoContainer]` — the enclosing
        // class qualifier is dropped at the call site.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            [NoContainer]
            public static class Constants
            {
                public static double Pi => 3.14;
            }
            """,
            """
            [assembly: TranspileAssembly]

            public class Circle
            {
                public double GetPi() => Constants.Pi;
            }
            """
        );

        var output = result["circle.ts"];
        await Assert.That(output).Contains("return pi;");
        await Assert.That(output).DoesNotContain("Constants.");
    }

    [Test]
    public async Task NoContainer_Methods_EmitAsTopLevelExports()
    {
        // Plain methods on an `[NoContainer]` static class lower to
        // top-level `export function` declarations inside a file named
        // after the class. The TypeScript class wrapper is dropped so
        // the call-site flatten (`MathUtils.Add` → `add`) references
        // a defined export.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class MathUtils
            {
                public static int Add(int a, int b) => a + b;
            }
            """
        );

        await Assert.That(result).ContainsKey("math-utils.ts");
        var output = result["math-utils.ts"];
        await Assert.That(output).Contains("export function add(a: number, b: number)");
        await Assert.That(output).DoesNotContain("class MathUtils");
    }

    [Test]
    public async Task NoContainer_MethodCall_AccessFlattensAtCallSite()
    {
        // `MathUtils.Add(1, 2)` on the C# side lowers to `add(1, 2)`
        // at the call site — the enclosing class qualifier is dropped
        // and the generated top-level function is the import target.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class MathUtils
            {
                public static int Add(int a, int b) => a + b;
            }

            public class Calculator
            {
                public int Sum() => MathUtils.Add(1, 2);
            }
            """
        );

        var output = result["calculator.ts"];
        await Assert.That(output).Contains("add(1, 2)");
        await Assert.That(output).DoesNotContain("MathUtils.add");
    }

    [Test]
    public async Task NoContainer_CrossModuleCall_EmitsFunctionImport()
    {
        // Cross-file consumer of an `[NoContainer]` static method must
        // import the camelCased function name (NOT the type name) from
        // the file the function was emitted into. Without the export
        // registry the import collector keys off type names only, and
        // a flattened lowercase callee leaks as `Cannot find name` at
        // type-check time.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [NoContainer]
            public static class MathUtils
            {
                public static int Add(int a, int b) => a + b;
            }

            public class Calculator
            {
                public int Sum() => MathUtils.Add(1, 2);
            }
            """
        );

        var output = result["calculator.ts"];
        await Assert.That(output).Contains("import { add } from \"./math-utils.js\"");
    }

    // ─── Diagnostics (MS0015) ────────────────────────────────

    [Test]
    public async Task NoContainer_OnNonStaticClass_EmitsMs0015()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [NoContainer]
            public class NotStatic {}
            """
        );

        var ms0015 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidNoContainer);
        await Assert.That(ms0015).IsNotNull();
        await Assert.That(ms0015!.Message).Contains("static");
    }

    [Test]
    public async Task NoContainer_MethodGroupReference_EmitsImport()
    {
        // #179: a method-group reference to a [NoContainer] static method
        // (delegate position, lambda capture, callback arg) lowers to a
        // bare lowercase identifier. ImportCollector's TsIdentifier branch
        // used to filter by `char.IsUpper(name[0])`, so the lowercase name
        // never reached the import line — `tsc` then errored on the
        // unresolved identifier. The branch must accept lowercase when the
        // name matches a known NoContainer function export.
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations;

            [Transpile, NoContainer]
            public static class Reducers
            {
                public static int Increment(int x) => x + 1;
            }

            [Transpile]
            public class App
            {
                public Func<int, int> Bind() => Reducers.Increment;
            }
            """
        );

        var output = result["app.ts"];
        await Assert.That(output).Contains("return increment;");
        await Assert.That(output).Contains("import { increment } from \"./reducers.js\"");
    }

    [Test]
    public async Task NoContainer_CrossAssembly_StaticMethodCall_EmitsImport()
    {
        // #178: a [NoContainer] static class shipped from a referenced
        // [EmitPackage] assembly must surface its flattened call sites at
        // the consumer's import line. Without cross-assembly scanning, the
        // consumer file calls `foo(1)` but emits no import for it.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("ui-lib")]

            namespace UiLib
            {
                [NoContainer]
                public static class UI
                {
                    public static int Foo(int x) => x;
                }
            }
            """,
            """
            using UiLib;

            [assembly: TranspileAssembly]

            public class Caller
            {
                public int Use() => UI.Foo(1);
            }
            """
        );

        var output = result["caller.ts"];
        await Assert.That(output).Contains("foo(1)");
        await Assert.That(output).Contains("from \"ui-lib");
        await Assert.That(output).Contains("foo");
        // Flattened call site (no qualifier).
        await Assert.That(output).DoesNotContain("UI.foo");
        await Assert.That(output).DoesNotContain("UI.Foo");
    }

    [Test]
    public async Task NoContainer_WithTranspile_EmitsModuleStyle()
    {
        // `[Transpile]` forces discovery inside binding projects that
        // do not opt into assembly-wide transpilation; `[NoContainer]`
        // still dictates call-site scope erasure + module-style
        // emission. The combo is valid so catalog/extension helpers
        // shipped from a referenced binding project can be authored
        // without `[assembly: TranspileAssembly]`.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [Transpile]
            [NoContainer]
            public static class Bridge
            {
                public static int Double(int value) => value * 2;
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidNoContainer))
            .IsFalse();

        var output = files["bridge.ts"];
        await Assert.That(output).Contains("export function double(value: number): number");
        await Assert.That(output).DoesNotContain("class Bridge");
    }
}
