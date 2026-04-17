using Metano.Compiler.Extraction;
using Metano.TypeScript;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests.IR;

/// <summary>
/// Exercises <see cref="IrToTsModuleBridge"/> end-to-end: compiles a C#
/// static class (with or without <c>[ExportedAsModule]</c>), runs the IR
/// module-function extractor, feeds the functions through the bridge, and
/// prints the resulting file so we can pin the top-level function shape.
/// </summary>
public class IrToTsModuleBridgeTests
{
    [Test]
    public async Task ExportedAsModule_StaticMethods_BecomeTopLevelFunctions()
    {
        var output = BuildModule(
            """
            [Transpile, ExportedAsModule]
            public static class MathUtils
            {
                public static int Double(int x) => x * 2;
                public static int Increment(int x) => x + 1;
            }
            """,
            "MathUtils"
        );

        // A `[ExportedAsModule]` static class renders as a flat file of
        // exported functions — no `class MathUtils { … }` wrapper.
        await Assert.That(output).Contains("export function double(x: number): number");
        await Assert.That(output).Contains("export function increment(x: number): number");
        await Assert.That(output).DoesNotContain("class MathUtils");
    }

    [Test]
    public async Task AsyncMethod_PreservesAsyncKeyword()
    {
        var output = BuildModule(
            """
            [Transpile, ExportedAsModule]
            public static class IO
            {
                public static async System.Threading.Tasks.Task<int> LoadAsync() => await System.Threading.Tasks.Task.FromResult(42);
            }
            """,
            "IO"
        );

        await Assert.That(output).Contains("export async function loadAsync()");
    }

    [Test]
    public async Task ClassicExtensionMethod_RendersReceiverAsFirstParam()
    {
        // Classic extension methods (`this T param`) are just static methods
        // Roslyn tags with IsExtensionMethod. The bridge emits them verbatim
        // — the receiver becomes the first TS parameter exactly like the
        // legacy ModuleTransformer produced.
        var output = BuildModule(
            """
            public static class StringExt
            {
                public static int Length2(this string s) => s.Length * 2;
            }
            """,
            "StringExt"
        );

        await Assert.That(output).Contains("export function length2(s: string): number");
    }

    [Test]
    public async Task GenericMethod_CarriesTypeParameters()
    {
        // Generic exported-module methods must declare their type parameters
        // on the emitted function signature — otherwise the parameter / return
        // types reference an undeclared T and the TS fails to compile.
        var output = BuildModule(
            """
            [Transpile, ExportedAsModule]
            public static class GenericsModule
            {
                public static T Identity<T>(T value) => value;
            }
            """,
            "GenericsModule"
        );

        await Assert.That(output).Contains("export function identity<T>(value: T): T");
    }

    [Test]
    public async Task NameOverride_IsHonoredOnTopLevelFunction()
    {
        // [Name(...)] and [Name(TargetLanguage.TypeScript, ...)] must survive
        // the IR → TS round-trip; otherwise renames on module functions regress.
        var output = BuildModule(
            """
            [Transpile, ExportedAsModule]
            public static class MathUtils
            {
                [Name("sum")]
                public static int Add(int a, int b) => a + b;
            }
            """,
            "MathUtils"
        );

        await Assert.That(output).Contains("export function sum(a: number, b: number)");
        await Assert.That(output).DoesNotContain("function add(");
    }

    [Test]
    public async Task IteratorMethod_LowersToGeneratorFunction()
    {
        // Methods that use `yield` become `function*` with a `Generator<T>`
        // return type. The async flag is forced off by the extractor since a
        // single method can't be both a generator and async on this backend.
        var output = BuildModule(
            """
            [Transpile, ExportedAsModule]
            public static class Iterators
            {
                public static System.Collections.Generic.IEnumerable<int> OneTwo()
                {
                    yield return 1;
                    yield return 2;
                }
            }
            """,
            "Iterators"
        );

        await Assert.That(output).Contains("export function* oneTwo()");
        await Assert.That(output).Contains("Generator<number>");
    }

    [Test]
    public async Task EmptyModule_ProducesNoFunctions()
    {
        var output = BuildModule(
            """
            [Transpile, ExportedAsModule]
            public static class Empty { }
            """,
            "Empty"
        );

        await Assert.That(output.Trim()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ExtensionBlock_EmitsOneFunctionPerMemberWithReceiver()
    {
        // C# 14 `extension(Receiver r) { … }` — every member inside the
        // block becomes a top-level function whose first parameter is the
        // block's receiver. Extension properties collapse to getter-shaped
        // functions with only the receiver in the signature.
        var output = BuildModule(
            """
            public static class IntExtensions
            {
                extension(int x)
                {
                    public int Doubled() => x * 2;
                    public int Squared => x * x;
                }
            }
            """,
            "IntExtensions"
        );
        await Assert.That(output).Contains("export function doubled(x: number): number");
        await Assert.That(output).Contains("export function squared(x: number): number");
    }

    [Test]
    public async Task ClassicExtensionMethod_EmitsReceiverAsFirstParameter()
    {
        // Roslyn exposes `public static int Double(this int x)` with
        // IsExtensionMethod = true and Parameters = [x]. The IR extractor
        // treats it as an ordinary function; the bridge emits the same
        // flat `export function double(x: number): number` shape so callers
        // can `import { double }` and call it positionally instead of going
        // through an `int.prototype.double()` style that TS can't support.
        var output = BuildModule(
            """
            public static class IntExtensions
            {
                public static int Double(this int x) => x * 2;
            }
            """,
            "IntExtensions"
        );
        await Assert.That(output).Contains("export function double(x: number): number");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string BuildModule(string classSource, string className)
    {
        var compilation = IrTestHelper.Compile(classSource);
        var tree = compilation.SyntaxTrees.First();
        var typeDecl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var model = compilation.GetSemanticModel(tree);
        var typeSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(typeDecl)!;

        var functions = IrModuleFunctionExtractor.Extract(
            typeSymbol,
            originResolver: null,
            compilation: compilation
        );

        var statements = new List<TsTopLevel>();
        IrToTsModuleBridge.Convert(functions, statements);

        var file = new TsSourceFile($"{className.ToLowerInvariant()}.ts", statements);
        return new Printer().Print(file);
    }
}
