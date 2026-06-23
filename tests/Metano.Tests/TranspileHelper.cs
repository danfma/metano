using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// Helper that compiles inline C# code and transpiles it to TypeScript using Metano.
/// <para>
/// Metadata references are built once per test run (see <see cref="BaseReferences"/>).
/// Before caching, every test re-scanned the runtime directory and created ~200
/// <see cref="MetadataReference"/>s from scratch — with 450+ tests that amounts to
/// ~100k metadata loads per run and the corresponding memory churn. Sharing a single
/// read-only list keeps the same compilation semantics while trimming both memory
/// and wall-clock time.
/// </para>
/// </summary>
public static class TranspileHelper
{
    /// <summary>
    /// Shared base set of metadata references — the runtime BCL (everything under the
    /// current runtime directory) plus Metano.Annotations. Built exactly once per
    /// test process and reused across every <see cref="Transpile"/> /
    /// <see cref="CompileLibrary"/> / <see cref="TranspileWithLibrary"/> call.
    /// </summary>
    internal static IReadOnlyList<MetadataReference> BaseReferences { get; } =
        BuildBaseReferences();

    private static List<MetadataReference> BuildBaseReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(TranspileAttribute).Assembly.Location),
        };
        var seen = new HashSet<string>(references.Select(r => r.Display!), StringComparer.Ordinal);
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            if (!seen.Add(dll))
                continue;
            try
            {
                references.Add(MetadataReference.CreateFromFile(dll));
            }
            catch
            {
                // Skip assemblies that can't be loaded (design-time-only, ref-only, etc.).
            }
        }
        var netstandardPath = Path.Combine(runtimeDir, "netstandard.dll");
        if (File.Exists(netstandardPath) && seen.Add(netstandardPath))
            references.Add(MetadataReference.CreateFromFile(netstandardPath));
        return references;
    }

    private static SyntaxTree ParseSource(string csharpSource)
    {
        var source = $"""
            using System;
            using System.Threading.Tasks;
            using Metano.Annotations;
            {csharpSource}
            """;
        return CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
    }

    private static CSharpCompilation CompileAssembly(
        string csharpSource,
        string assemblyName,
        OutputKind outputKind,
        IEnumerable<MetadataReference>? extraReferences = null
    )
    {
        var tree = ParseSource(csharpSource);
        var references = extraReferences is null
            ? (IEnumerable<MetadataReference>)BaseReferences
            : BaseReferences.Concat(extraReferences);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(outputKind)
        );
        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"C# compilation ({assemblyName}) failed:\n"
                    + string.Join("\n", errors.Select(e => e.ToString()))
            );
        return compilation;
    }

    /// <summary>
    /// Builds a <see cref="TypeTransformer"/> wired to the IR the
    /// <see cref="CSharpSourceFrontend"/> would produce in production. Lets
    /// individual tests poke at the transformer's internal state (e.g.,
    /// <c>CrossPackageDependencies</c>) without having to recreate the IR
    /// extraction boilerplate.
    /// </summary>
    public static TypeTransformer NewTransformer(CSharpCompilation compilation) =>
        NewTransformerWithIr(compilation).Transformer;

    /// <summary>
    /// Variant of <see cref="NewTransformer"/> that also returns the
    /// <see cref="IrCompilation"/> produced by the frontend. Tests that
    /// inspect diagnostics raised during extraction (for example, cross-
    /// assembly <c>[Import]</c> collisions) merge <c>ir.Diagnostics</c>
    /// with <c>transformer.Diagnostics</c> to see what the host would
    /// ultimately report.
    /// </summary>
    public static (IrCompilation Ir, TypeTransformer Transformer) NewTransformerWithIr(
        CSharpCompilation compilation
    )
    {
        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);
        return (ir, new TypeTransformer(ir, compilation));
    }

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
    ) TranspileCore(
        CSharpCompilation compilation,
        bool useIrBodies = true,
        bool namespaceBarrels = false,
        bool stripInterfacePrefix = false,
        string? importAlias = null
    )
    {
        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation);
        var transformer = new TypeTransformer(ir, compilation)
        {
            UseIrBodiesWhenCovered = useIrBodies,
            NamespaceBarrels = namespaceBarrels,
            StripInterfacePrefix = stripInterfacePrefix,
            ImportAlias = importAlias,
        };
        var files = transformer.TransformAll();
        var printer = new Printer();
        var result = new Dictionary<string, string>();
        foreach (var file in files)
            result[file.FileName] = printer.Print(file);
        // Mirror the production host merge — frontend-raised diagnostics
        // (extraction-time validations like MS0010) need to surface to
        // test callers alongside the transformer's own output.
        var diagnostics =
            ir.Diagnostics.Count == 0
                ? (IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic>)
                    transformer.Diagnostics
                : ir.Diagnostics.Concat(transformer.Diagnostics).ToList();
        return (result, diagnostics);
    }

    /// <summary>
    /// Compiles C# source code and transpiles all [Transpile]-annotated types.
    /// Returns a dictionary of filename → TypeScript content.
    /// </summary>
    public static Dictionary<string, string> Transpile(
        string csharpSource,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        string? importAlias = null
    )
    {
        var compilation = CompileAssembly(csharpSource, "TestAssembly", outputKind);
        return TranspileCore(compilation, importAlias: importAlias).Files;
    }

    /// <summary>
    /// Like <see cref="Transpile"/> but uses <c>OutputKind.ConsoleApplication</c> so C# 9+
    /// top-level statements are permitted.
    /// </summary>
    public static Dictionary<string, string> TranspileConsoleApp(string csharpSource) =>
        Transpile(csharpSource, OutputKind.ConsoleApplication);

    /// <summary>
    /// The SolidJS + DOM binding assemblies, added as metadata references only
    /// for the JSX golden tests via <see cref="TranspileJsx"/>. Kept off the
    /// shared <see cref="BaseReferences"/> on purpose: the SolidJS binding
    /// carries <c>[assembly: TranspileAssembly] + [EmitPackage]</c> and
    /// <c>[Import]</c> members, so folding it into every compilation would leak
    /// cross-assembly external-import + transpilable-type discovery into
    /// unrelated frontend tests.
    /// </summary>
    private static readonly MetadataReference[] JsxBindingReferences =
    [
        MetadataReference.CreateFromFile(
            typeof(Metano.TypeScript.SolidJs.JsxComponent).Assembly.Location
        ),
        MetadataReference.CreateFromFile(typeof(Metano.TypeScript.DOM.Document).Assembly.Location),
    ];

    /// <summary>
    /// Compiles inline C# against the real SolidJS + DOM bindings and transpiles
    /// it. Used by the JSX golden tests so the emitted <c>.tsx</c> is
    /// authoritative against the shipped binding (e.g. the <c>[Name("class")]</c>
    /// override on <c>Html.Element.ClassName</c>) rather than an inline stand-in.
    /// </summary>
    public static Dictionary<string, string> TranspileJsx(string csharpSource)
    {
        var compilation = CompileAssembly(
            csharpSource,
            "TestAssembly",
            OutputKind.DynamicallyLinkedLibrary,
            extraReferences: JsxBindingReferences
        );
        return TranspileCore(compilation).Files;
    }

    /// <summary>
    /// Like <see cref="TranspileJsx"/> but also returns the merged diagnostics
    /// (frontend + transformer), so JSX validation tests can assert MS0026 when
    /// a non-renderable type lands in a JSX-renderable position.
    /// </summary>
    public static (
        Dictionary<string, string> Files,
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
    ) TranspileJsxWithDiagnostics(string csharpSource)
    {
        var compilation = CompileAssembly(
            csharpSource,
            "TestAssembly",
            OutputKind.DynamicallyLinkedLibrary,
            extraReferences: JsxBindingReferences
        );
        return TranspileCore(compilation);
    }

    /// <summary>
    /// Variant of <see cref="Transpile"/> that enables the
    /// <c>--namespace-barrels</c> opt-in — exercises the root
    /// <c>src/index.ts</c> emission path with nested
    /// <c>export namespace</c> blocks.
    /// </summary>
    public static Dictionary<string, string> TranspileWithNamespaceBarrels(string csharpSource)
    {
        var compilation = CompileAssembly(
            csharpSource,
            "TestAssembly",
            OutputKind.DynamicallyLinkedLibrary
        );
        return TranspileCore(compilation, namespaceBarrels: true).Files;
    }

    /// <summary>
    /// Variant of <see cref="Transpile"/> that enables the opt-in
    /// <c>--strip-interface-prefix</c> flag. Returns both the emitted
    /// files and the merged diagnostics so collision tests can
    /// assert <c>MS0017</c> when the strip cannot apply.
    /// </summary>
    public static (
        Dictionary<string, string> Files,
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
    ) TranspileWithStripInterfacePrefix(string csharpSource)
    {
        var compilation = CompileAssembly(
            csharpSource,
            "TestAssembly",
            OutputKind.DynamicallyLinkedLibrary
        );
        return TranspileCore(compilation, stripInterfacePrefix: true);
    }

    /// <summary>
    /// Like <see cref="Transpile"/> but enables the Phase 5.10b IR-driven body pipeline.
    /// Used by integration tests that pin the IR path's output; production samples keep
    /// the default legacy path until IR coverage is complete.
    /// </summary>
    public static Dictionary<string, string> TranspileWithIrBodies(string csharpSource)
    {
        var compilation = CompileAssembly(
            csharpSource,
            "TestAssembly",
            OutputKind.DynamicallyLinkedLibrary
        );
        return TranspileCore(compilation, useIrBodies: true).Files;
    }

    /// <summary>
    /// Compiles C# source code, transpiles it, and returns both the generated files and
    /// any diagnostics emitted by the transformer.
    /// </summary>
    public static (
        Dictionary<string, string> Files,
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
    ) TranspileWithDiagnostics(string csharpSource, string? importAlias = null)
    {
        var compilation = CompileAssembly(
            csharpSource,
            "TestAssembly",
            OutputKind.DynamicallyLinkedLibrary
        );
        return TranspileCore(compilation, importAlias: importAlias);
    }

    /// <summary>
    /// Compiles two C# sources as separate assemblies (a "library" and a "consumer"),
    /// where the consumer references the library, then transpiles the consumer. Used to
    /// validate cross-assembly type discovery and import resolution.
    /// </summary>
    public static Dictionary<string, string> TranspileWithLibrary(
        string librarySource,
        string consumerSource
    ) => TranspileWithLibraryCore(librarySource, consumerSource).Files;

    /// <summary>
    /// Cross-package variant of <see cref="TranspileJsx"/>: compiles a JSX-aware
    /// library (carrying its own <c>[EmitPackage]</c>) and a consumer that references
    /// it, both against the SolidJS + DOM bindings, then transpiles the consumer.
    /// Pins that a JSX component tag from a referenced package resolves through the
    /// cross-package import channel rather than a dangling intra-project import.
    /// </summary>
    public static Dictionary<string, string> TranspileJsxWithLibrary(
        string librarySource,
        string consumerSource
    )
    {
        var libCompilation = CompileAssembly(
            librarySource,
            "TestLibrary",
            OutputKind.DynamicallyLinkedLibrary,
            extraReferences: JsxBindingReferences
        );
        var consumerCompilation = CompileAssembly(
            consumerSource,
            "TestConsumer",
            OutputKind.DynamicallyLinkedLibrary,
            extraReferences: JsxBindingReferences.Append(libCompilation.ToMetadataReference())
        );
        return TranspileCore(consumerCompilation).Files;
    }

    /// <summary>
    /// Same as <see cref="TranspileWithLibrary"/> but also returns the diagnostics
    /// emitted by the consumer's transformation. Used for tests that assert MS00xx
    /// codes around cross-package resolution.
    /// </summary>
    public static (
        Dictionary<string, string> Files,
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
    ) TranspileWithLibraryAndDiagnostics(string librarySource, string consumerSource) =>
        TranspileWithLibraryCore(librarySource, consumerSource);

    /// <summary>
    /// Compiles a library source into an in-memory <see cref="CSharpCompilation"/>.
    /// Useful for tests that want to inspect the cross-package transformer state
    /// directly (e.g., <c>CrossPackageDependencies</c>) instead of just asserting on
    /// the generated TS files.
    /// </summary>
    public static CSharpCompilation CompileLibrary(string librarySource) =>
        CompileLibrary(librarySource, "TestLibrary");

    /// <summary>
    /// Compiles a library source into an in-memory <see cref="CSharpCompilation"/>
    /// using the provided assembly name. Useful for tests that need multiple distinct
    /// referenced libraries in the same consumer compilation.
    /// </summary>
    public static CSharpCompilation CompileLibrary(string librarySource, string assemblyName) =>
        CompileAssembly(librarySource, assemblyName, OutputKind.DynamicallyLinkedLibrary);

    /// <summary>
    /// Compiles a consumer source that references a previously built library
    /// compilation. The consumer's references include the base set plus the library
    /// as a metadata reference (in-memory).
    /// </summary>
    public static CSharpCompilation CompileConsumer(
        string consumerSource,
        CSharpCompilation libraryCompilation
    ) => CompileConsumer(consumerSource, [libraryCompilation]);

    /// <summary>
    /// Compiles a consumer source that references multiple previously built library
    /// compilations. Useful for tests that need to validate behavior across more than
    /// one referenced assembly.
    /// </summary>
    public static CSharpCompilation CompileConsumer(
        string consumerSource,
        params CSharpCompilation[] libraryCompilations
    ) =>
        CompileAssembly(
            consumerSource,
            "TestConsumer",
            OutputKind.DynamicallyLinkedLibrary,
            extraReferences: libraryCompilations.Select(c => c.ToMetadataReference())
        );

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
    ) TranspileWithLibraryCore(string librarySource, string consumerSource)
    {
        var libCompilation = CompileLibrary(librarySource);
        var consumerCompilation = CompileConsumer(consumerSource, libCompilation);
        return TranspileCore(consumerCompilation);
    }

    /// <summary>
    /// Reads an expected .ts file from the Expected/ directory.
    /// </summary>
    public static string ReadExpected(string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Expected", filename);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Expected file not found: {path}");
        return File.ReadAllText(path);
    }

    public static (
        Dictionary<string, string> Files,
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
    ) TranspileDart(string csharpSource, string assemblyName = "DartTestAssembly")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            csharpSource,
            new CSharpParseOptions(LanguageVersion.Preview)
        );
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "C# compilation failed:\n" + string.Join("\n", errors.Select(e => e.ToString()))
            );
        var ir = new CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            TargetLanguage.Dart
        );
        var transformer = new Metano.Compiler.Dart.Transformation.DartTransformer(ir, compilation);
        var files = transformer.TransformAll();
        var printer = new Metano.Compiler.Dart.Printer();
        var result = new Dictionary<string, string>();
        foreach (var file in files)
            result[file.FileName] = printer.Print(file);
        return (result, transformer.Diagnostics);
    }
}
