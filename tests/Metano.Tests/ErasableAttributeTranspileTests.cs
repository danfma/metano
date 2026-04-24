using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for <c>[Erasable]</c> from <c>Metano.Annotations</c>. The
/// attribute marks a static class whose scope vanishes at the call
/// site: the class emits no file, static member access flattens to a
/// bare identifier. Members inside emit per their own attributes.
/// Covers the flatten, the no-file contract, and the MS0015
/// validation surface.
/// </summary>
public class ErasableAttributeTranspileTests
{
    // ─── Emission flatten ────────────────────────────────────

    [Test]
    public async Task Erasable_StaticMember_AccessFlattensToIdentifier()
    {
        // `Constants.Pi` on the C# side should lower to a bare `pi`
        // identifier on the TS side (camelCased per the TS naming
        // policy) because `Constants` is `[Erasable]` — the enclosing
        // class qualifier is dropped at the call site.
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            [Erasable]
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
    public async Task Erasable_Class_EmitsNoFile()
    {
        // `[Erasable]` classes are compile-time sugar — no .ts file
        // emits for them even when the class lives in a transpilable
        // assembly.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Erasable]
            public static class Constants
            {
                public static double Pi => 3.14;
            }

            public class Placeholder {}
            """
        );

        await Assert.That(result).DoesNotContainKey("constants.ts");
        await Assert.That(result).ContainsKey("placeholder.ts");
    }

    // ─── Diagnostics (MS0015) ────────────────────────────────

    [Test]
    public async Task Erasable_OnNonStaticClass_EmitsMs0015()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [Erasable]
            public class NotStatic {}
            """
        );

        var ms0015 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidErasable);
        await Assert.That(ms0015).IsNotNull();
        await Assert.That(ms0015!.Message).Contains("static");
    }

    [Test]
    public async Task Erasable_WithTranspile_EmitsMs0015()
    {
        // The two attributes are semantically incompatible — one asks
        // for no emission, the other asks for full emission. MS0015
        // surfaces the conflict at extraction time.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [Transpile]
            [Erasable]
            public static class Conflict
            {
                public static int X => 42;
            }
            """
        );

        var ms0015 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidErasable);
        await Assert.That(ms0015).IsNotNull();
        await Assert.That(ms0015!.Message).Contains("[Transpile]");
    }
}
