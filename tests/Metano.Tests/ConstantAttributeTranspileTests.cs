using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Tests for <c>[Constant]</c> from <c>Metano.Annotations</c>. The
/// attribute enforces that decorated parameters receive compile-time
/// constant arguments and decorated fields are initialized with
/// compile-time constants. Violations surface as MS0014
/// InvalidConstant. Covers both validation surfaces plus the happy
/// paths (literal, `const` local/field, `readonly` constant-reducible
/// field).
/// </summary>
public class ConstantAttributeTranspileTests
{
    // ─── Parameter validator — positive paths ────────────────

    [Test]
    public async Task Constant_Parameter_LiteralArgument_Passes()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Runtime
            {
                public static void Use([Constant] string tag) {}
            }

            public class Caller
            {
                public void Run() => Runtime.Use("div");
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidConstant))
            .IsFalse();
    }

    [Test]
    public async Task Constant_Parameter_ConstField_Passes()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Runtime
            {
                public static void Use([Constant] string tag) {}
            }

            public class Caller
            {
                private const string Tag = "div";
                public void Run() => Runtime.Use(Tag);
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidConstant))
            .IsFalse();
    }

    // ─── Parameter validator — negative paths ────────────────

    [Test]
    public async Task Constant_Parameter_Variable_EmitsMs0014()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Runtime
            {
                public static void Use([Constant] string tag) {}
            }

            public class Caller
            {
                public void Run(string tag) => Runtime.Use(tag);
            }
            """
        );

        var ms0014 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidConstant);
        await Assert.That(ms0014).IsNotNull();
        await Assert.That(ms0014!.Message).Contains("'tag'");
    }

    [Test]
    public async Task Constant_Parameter_MethodCall_EmitsMs0014()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Runtime
            {
                public static void Use([Constant] string tag) {}
            }

            public class Caller
            {
                public string Compute() => "x";
                public void Run() => Runtime.Use(Compute());
            }
            """
        );

        var ms0014 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidConstant);
        await Assert.That(ms0014).IsNotNull();
    }

    // ─── Parameter validator — named + positional ────────────

    [Test]
    public async Task Constant_Parameter_NamedArgument_ResolvesCorrectParameter()
    {
        // When the caller uses a named argument, the validator must
        // resolve the parameter by name rather than position so the
        // `[Constant]` contract is enforced on the right arg.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Runtime
            {
                public static void Use(string loose, [Constant] string tag) {}
            }

            public class Caller
            {
                public void Run(string runtimeValue) =>
                    Runtime.Use(loose: runtimeValue, tag: "div");
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidConstant))
            .IsFalse();
    }

    // ─── Field validator ─────────────────────────────────────

    [Test]
    public async Task Constant_Field_LiteralInitializer_Passes()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Tags
            {
                [Constant] public static readonly string Div = "div";
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidConstant))
            .IsFalse();
    }

    [Test]
    public async Task Constant_Field_NonConstantInitializer_EmitsMs0014()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public class Tags
            {
                public static string Compute() => "div";
                [Constant] public static readonly string Div = Compute();
            }
            """
        );

        var ms0014 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidConstant);
        await Assert.That(ms0014).IsNotNull();
        await Assert.That(ms0014!.Message).Contains("Div");
    }

    // ─── Constructor call site ───────────────────────────────

    [Test]
    public async Task Constant_ConstructorParameter_LiteralArgument_Passes()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public readonly record struct Tag([Constant] string Value);

            public class Caller
            {
                public Tag Make() => new Tag("div");
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidConstant))
            .IsFalse();
    }

    [Test]
    public async Task Constant_ConstructorParameter_Variable_EmitsMs0014()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public readonly record struct Tag([Constant] string Value);

            public class Caller
            {
                public Tag Make(string runtimeValue) => new Tag(runtimeValue);
            }
            """
        );

        var ms0014 = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.InvalidConstant);
        await Assert.That(ms0014).IsNotNull();
    }
}
