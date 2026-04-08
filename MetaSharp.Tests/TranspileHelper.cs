using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MetaSharp.Annotations;
using MetaSharp.Transformation;
using MetaSharp.TypeScript;

namespace MetaSharp.Tests;

/// <summary>
/// Helper that compiles inline C# code and transpiles it to TypeScript using MetaSharp.
/// </summary>
public static class TranspileHelper
{
    private static readonly string MetaSharpAssemblyPath =
        typeof(TranspileAttribute).Assembly.Location;

    /// <summary>
    /// Compiles C# source code and transpiles all [Transpile]-annotated types.
    /// Returns a dictionary of filename → TypeScript content.
    /// </summary>
    public static Dictionary<string, string> Transpile(string csharpSource)
    {
        var source = $"""
            using System;
            using System.Threading.Tasks;
            using MetaSharp.Annotations;
            {csharpSource}
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.Preview));

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(TranspileAttribute).Assembly.Location),
        };

        // Add all runtime assemblies for netcoreapp
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            if (references.Any(r => r.Display == dll)) continue;
            try
            {
                references.Add(MetadataReference.CreateFromFile(dll));
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }
        }

        // netstandard facade (needed for MetaSharp targeting netstandard2.0)
        var netstandardPath = Path.Combine(runtimeDir, "netstandard.dll");
        if (File.Exists(netstandardPath) && references.All(r => r.Display != netstandardPath))
            references.Add(MetadataReference.CreateFromFile(netstandardPath));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        // Check for errors
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            var messages = string.Join("\n", errors.Select(e => e.ToString()));
            throw new InvalidOperationException($"C# compilation failed:\n{messages}");
        }

        var transformer = new TypeTransformer(compilation);
        var files = transformer.TransformAll();
        var printer = new Printer();

        var result = new Dictionary<string, string>();
        foreach (var file in files)
        {
            result[file.FileName] = printer.Print(file);
        }

        return result;
    }

    /// <summary>
    /// Compiles C# source code, transpiles it, and returns both the generated files and
    /// any diagnostics emitted by the transformer.
    /// </summary>
    public static (Dictionary<string, string> Files, IReadOnlyList<MetaSharp.Compiler.Diagnostics.MetaSharpDiagnostic> Diagnostics)
        TranspileWithDiagnostics(string csharpSource)
    {
        var source = $"""
            using System;
            using System.Threading.Tasks;
            using MetaSharp.Annotations;
            {csharpSource}
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.Preview));

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(TranspileAttribute).Assembly.Location),
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            if (references.Any(r => r.Display == dll)) continue;
            try { references.Add(MetadataReference.CreateFromFile(dll)); }
            catch { }
        }

        var netstandardPath = Path.Combine(runtimeDir, "netstandard.dll");
        if (File.Exists(netstandardPath) && references.All(r => r.Display != netstandardPath))
            references.Add(MetadataReference.CreateFromFile(netstandardPath));

        var compilation = CSharpCompilation.Create(
            "TestAssembly", [syntaxTree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var transformer = new TypeTransformer(compilation);
        var files = transformer.TransformAll();
        var printer = new Printer();

        var result = new Dictionary<string, string>();
        foreach (var file in files)
        {
            result[file.FileName] = printer.Print(file);
        }

        return (result, transformer.Diagnostics);
    }

    /// <summary>
    /// Compiles two C# sources as separate assemblies (a "library" and a "consumer"),
    /// where the consumer references the library, then transpiles the consumer. Used to
    /// validate cross-assembly type discovery and import resolution. The library source
    /// is wrapped with the same standard usings as <see cref="Transpile"/>.
    /// </summary>
    /// <summary>
    /// Same as <see cref="TranspileWithLibrary"/> but also returns the diagnostics
    /// emitted by the consumer's transformation. Used for tests that assert MS00xx
    /// codes around cross-package resolution.
    /// </summary>
    public static (Dictionary<string, string> Files, IReadOnlyList<MetaSharp.Compiler.Diagnostics.MetaSharpDiagnostic> Diagnostics)
        TranspileWithLibraryAndDiagnostics(string librarySource, string consumerSource)
    {
        var (files, diagnostics) = TranspileWithLibraryCore(librarySource, consumerSource);
        return (files, diagnostics);
    }

    public static Dictionary<string, string> TranspileWithLibrary(
        string librarySource, string consumerSource)
    {
        var (files, _) = TranspileWithLibraryCore(librarySource, consumerSource);
        return files;
    }

    private static (Dictionary<string, string> Files, IReadOnlyList<MetaSharp.Compiler.Diagnostics.MetaSharpDiagnostic> Diagnostics)
        TranspileWithLibraryCore(string librarySource, string consumerSource)
    {
        var libSource = $"""
            using System;
            using System.Threading.Tasks;
            using MetaSharp.Annotations;
            {librarySource}
            """;
        var consSource = $"""
            using System;
            using System.Threading.Tasks;
            using MetaSharp.Annotations;
            {consumerSource}
            """;

        var references = BuildBaseReferences();

        var libTree = CSharpSyntaxTree.ParseText(libSource,
            new CSharpParseOptions(LanguageVersion.Preview));
        var libCompilation = CSharpCompilation.Create(
            "TestLibrary", [libTree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var libErrors = libCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (libErrors.Count > 0)
            throw new InvalidOperationException(
                $"Library compilation failed:\n{string.Join("\n", libErrors)}");

        var consTree = CSharpSyntaxTree.ParseText(consSource,
            new CSharpParseOptions(LanguageVersion.Preview));
        var consumerCompilation = CSharpCompilation.Create(
            "TestConsumer",
            [consTree],
            references.Concat([libCompilation.ToMetadataReference()]),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var consErrors = consumerCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (consErrors.Count > 0)
            throw new InvalidOperationException(
                $"Consumer compilation failed:\n{string.Join("\n", consErrors)}");

        var transformer = new TypeTransformer(consumerCompilation);
        var files = transformer.TransformAll();
        var printer = new Printer();

        var result = new Dictionary<string, string>();
        foreach (var file in files)
            result[file.FileName] = printer.Print(file);
        return (result, transformer.Diagnostics);
    }

    private static List<MetadataReference> BuildBaseReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(TranspileAttribute).Assembly.Location),
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            if (references.Any(r => r.Display == dll)) continue;
            try { references.Add(MetadataReference.CreateFromFile(dll)); }
            catch { }
        }
        var netstandardPath = Path.Combine(runtimeDir, "netstandard.dll");
        if (File.Exists(netstandardPath) && references.All(r => r.Display != netstandardPath))
            references.Add(MetadataReference.CreateFromFile(netstandardPath));
        return references;
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
