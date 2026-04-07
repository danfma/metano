using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MetaSharp.Transformation;
using MetaSharp.TypeScript;

namespace MetaSharp.Tests;

/// <summary>
/// Helper that compiles inline C# code and transpiles it to TypeScript using MetaSharp.
/// </summary>
public static class TranspileHelper
{
    private static readonly string MetaSharpAnnotationsPath =
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
            using MetaSharp;
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

        // netstandard facade (needed for MetaSharp.Annotations targeting netstandard2.0)
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
            using MetaSharp;
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
