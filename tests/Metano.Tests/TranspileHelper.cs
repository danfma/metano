using Metano.Annotations;
using Metano.Transformation;
using Metano.TypeScript;
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

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
    ) TranspileCore(CSharpCompilation compilation, bool useIrBodies = true)
    {
        var transformer = new TypeTransformer(compilation) { UseIrBodiesWhenCovered = useIrBodies };
        var files = transformer.TransformAll();
        var printer = new Printer();
        var result = new Dictionary<string, string>();
        foreach (var file in files)
            result[file.FileName] = printer.Print(file);
        return (result, transformer.Diagnostics);
    }

    /// <summary>
    /// Compiles C# source code and transpiles all [Transpile]-annotated types.
    /// Returns a dictionary of filename → TypeScript content.
    /// </summary>
    public static Dictionary<string, string> Transpile(
        string csharpSource,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary
    )
    {
        var compilation = CompileAssembly(csharpSource, "TestAssembly", outputKind);
        return TranspileCore(compilation).Files;
    }

    /// <summary>
    /// Like <see cref="Transpile"/> but uses <c>OutputKind.ConsoleApplication</c> so C# 9+
    /// top-level statements are permitted.
    /// </summary>
    public static Dictionary<string, string> TranspileConsoleApp(string csharpSource) =>
        Transpile(csharpSource, OutputKind.ConsoleApplication);

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
    ) TranspileWithDiagnostics(string csharpSource)
    {
        var compilation = CompileAssembly(
            csharpSource,
            "TestAssembly",
            OutputKind.DynamicallyLinkedLibrary
        );
        return TranspileCore(compilation);
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
}
