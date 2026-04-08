namespace MetaSharp.Tests;

/// <summary>
/// Tests for <c>[ExportVarFromBody]</c> on a <c>[ModuleEntryPoint]</c> method. The
/// attribute promotes a named local variable from the entry point body to a
/// module-level export. Behavior matrix:
///
/// <list type="bullet">
///   <item><c>AsDefault = false, InPlace = true</c> → folded named export at the
///   declaration site (<c>export const app = ...</c>)</item>
///   <item><c>AsDefault = false, InPlace = false</c> → declaration stays + trailing
///   <c>export { app };</c></item>
///   <item><c>AsDefault = true, InPlace = false</c> → declaration stays + trailing
///   <c>export default app;</c></item>
///   <item><c>AsDefault = true, InPlace = true</c> → MS0006 error (contradictory)</item>
/// </list>
/// </summary>
public class ExportVarFromBodyTests
{
    [Test]
    public async Task AsDefaultTrue_InPlaceFalse_EmitsTrailingExportDefault()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("app", AsDefault = true, InPlace = false)]
                public static void Main()
                {
                    var app = "hello";
                    System.Console.WriteLine(app);
                }
            }
            """
        );

        var output = result["program.ts"];
        await Assert.That(output).Contains("const app = \"hello\";");
        await Assert.That(output).Contains("console.log(app)");
        await Assert.That(output).Contains("export default app;");
        // The const declaration is NOT prefixed with `export` (trailing form).
        await Assert.That(output).DoesNotContain("export const app");
    }

    [Test]
    public async Task AsDefaultFalse_InPlaceTrue_EmitsExportConstAtDeclarationSite()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("app", InPlace = true)]
                public static void Main()
                {
                    var app = "hello";
                }
            }
            """
        );

        var output = result["program.ts"];
        await Assert.That(output).Contains("export const app = \"hello\";");
        // No trailing `export { app };` line.
        await Assert.That(output).DoesNotContain("export { app }");
        await Assert.That(output).DoesNotContain("export default");
    }

    [Test]
    public async Task AsDefaultFalse_InPlaceFalse_EmitsTrailingNamedExport()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("app")]
                public static void Main()
                {
                    var app = "hello";
                }
            }
            """
        );

        var output = result["program.ts"];
        await Assert.That(output).Contains("const app = \"hello\";");
        await Assert.That(output).Contains("export { app };");
        await Assert.That(output).DoesNotContain("export const app");
        await Assert.That(output).DoesNotContain("export default");
    }

    [Test]
    public async Task AsDefaultTrue_InPlaceTrue_EmitsDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("app", AsDefault = true, InPlace = true)]
                public static void Main()
                {
                    var app = "hello";
                }
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0006")).IsTrue();
    }

    [Test]
    public async Task ExportVarFromBody_LocalNotFound_EmitsDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("doesNotExist")]
                public static void Main()
                {
                    var app = "hello";
                }
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0006")).IsTrue();
    }

    [Test]
    public async Task BodyMutatesExportedVar_KeepsLetAndExports()
    {
        // The local var is reassigned, so it must be `let` (not `const`). The export
        // form should still work — TypeScript live-binds named exports.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint, ExportVarFromBody("greeting")]
                public static void Main()
                {
                    var greeting = "hello";
                    greeting = "world";
                }
            }
            """
        );

        var output = result["program.ts"];
        await Assert.That(output).Contains("let greeting = \"hello\";");
        await Assert.That(output).Contains("greeting = \"world\"");
        await Assert.That(output).Contains("export { greeting };");
    }
}
