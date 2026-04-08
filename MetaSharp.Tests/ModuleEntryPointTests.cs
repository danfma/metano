namespace MetaSharp.Tests;

/// <summary>
/// Tests for the <c>[ModuleEntryPoint]</c> attribute. Marks one static method on an
/// <c>[ExportedAsModule]</c> class so its body is unwrapped as the top-level executable
/// code of the generated TS module instead of being emitted as an exported function.
/// Other static methods on the same class continue to become regular exported functions.
/// </summary>
public class ModuleEntryPointTests
{
    [Test]
    public async Task EntryPointBody_LowersToTopLevelStatements()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint]
                public static void Main()
                {
                    var greeting = "Hello";
                    System.Console.WriteLine(greeting);
                }
            }
            """
        );

        var output = result["program.ts"];
        // The local var becomes top-level const (immutable, defaulting to const).
        await Assert.That(output).Contains("const greeting = \"Hello\"");
        await Assert.That(output).Contains("console.log(greeting)");
        // No `function main()` wrapper anywhere.
        await Assert.That(output).DoesNotContain("function main");
    }

    [Test]
    public async Task SiblingFunctions_StillExportedAsFunctions()
    {
        // The class has a [ModuleEntryPoint] AND a regular static function — both must
        // appear: the function as `export function helper()`, the entry point as
        // unwrapped top-level code.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                public static int Helper(int x) => x * 2;

                [ModuleEntryPoint]
                public static void Main()
                {
                    var n = Helper(21);
                }
            }
            """
        );

        var output = result["program.ts"];
        await Assert.That(output).Contains("export function helper");
        await Assert.That(output).Contains("const n = ");
        // The entry point method itself is NOT emitted as a function.
        await Assert.That(output).DoesNotContain("function main");
    }

    [Test]
    public async Task AsyncEntryPoint_PreservesAwaitAtTopLevel()
    {
        // Top-level await is native to ESM modules. async Task Main() should drop the
        // function wrapper and the `async` keyword, leaving the await expressions in the
        // module scope where they evaluate at import time.
        var result = TranspileHelper.Transpile(
            """
            using System.Threading.Tasks;

            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint]
                public static async Task Main()
                {
                    var value = await Task.FromResult(42);
                    System.Console.WriteLine(value);
                }
            }
            """
        );

        var output = result["program.ts"];
        await Assert.That(output).Contains("const value = await");
        await Assert.That(output).Contains("console.log(value)");
        await Assert.That(output).DoesNotContain("function main");
        // No `async` keyword on a function — the body is just statements.
        await Assert.That(output).DoesNotContain("async function");
    }

    [Test]
    public async Task EntryPointWithParameters_EmitsDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint]
                public static void Main(string[] args) { }
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0006")).IsTrue();
    }

    [Test]
    public async Task EntryPointWithNonTaskReturn_EmitsDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint]
                public static int Main() => 42;
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0006")).IsTrue();
    }

    [Test]
    public async Task MultipleEntryPoints_EmitsDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            public static class Program
            {
                [ModuleEntryPoint]
                public static void Main() { }

                [ModuleEntryPoint]
                public static void OtherMain() { }
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0006")).IsTrue();
    }
}
