using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests.IR;

/// <summary>
/// Helper that compiles inline C# code and extracts IR from the types.
/// </summary>
public static class IrTestHelper
{
    /// <summary>
    /// Compiles C# source, finds the first [Transpile]-annotated enum, and extracts it to IR.
    /// </summary>
    public static IrEnumDeclaration ExtractEnum(string csharpSource)
    {
        var compilation = Compile(csharpSource);
        var type = FindTranspilableType(compilation, [TypeKind.Enum]);
        return IrEnumExtractor.Extract(type);
    }

    /// <summary>
    /// Compiles C# source, finds the first [Transpile]-annotated interface, and extracts it to IR.
    /// </summary>
    public static IrInterfaceDeclaration ExtractInterface(string csharpSource)
    {
        var compilation = Compile(csharpSource);
        var type = FindTranspilableType(compilation, [TypeKind.Interface]);
        return IrInterfaceExtractor.Extract(type);
    }

    /// <summary>
    /// Compiles C# source and extracts the first matching
    /// [Transpile]-annotated class or struct (or one identified by
    /// <paramref name="typeName"/>) to <see cref="IrClassDeclaration"/>.
    /// Replaces the per-file <c>ExtractClass</c> helpers each test
    /// fixture used to maintain.
    /// </summary>
    public static IrClassDeclaration ExtractClass(string csharpSource, string? typeName = null)
    {
        var compilation = Compile(csharpSource);
        var type = FindTranspilableType(compilation, [TypeKind.Class, TypeKind.Struct], typeName);
        return IrClassExtractor.Extract(type);
    }

    /// <summary>
    /// Compiles C# source and maps a type by name to an IrTypeRef.
    /// </summary>
    public static IrTypeRef MapType(string csharpSource, string typeName)
    {
        var compilation = Compile(csharpSource);
        var type =
            compilation.GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"Type '{typeName}' not found");
        return IrTypeRefMapper.Map(type);
    }

    public static CSharpCompilation Compile(
        string csharpSource,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary
    )
    {
        var source = $"""
            using System;
            using System.Threading.Tasks;
            using System.Collections.Generic;
            using Metano.Annotations;
            {csharpSource}
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview)
        );

        // Reuse the process-wide cached runtime reference set built by
        // TranspileHelper — avoids rebuilding ~200 MetadataReferences per test.
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(outputKind)
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            var messages = string.Join("\n", errors.Select(e => e.ToString()));
            throw new InvalidOperationException($"C# compilation failed:\n{messages}");
        }

        return compilation;
    }

    /// <summary>
    /// Scans <paramref name="compilation"/> for the first declared
    /// <see cref="INamedTypeSymbol"/> whose <see cref="TypeKind"/>
    /// appears in <paramref name="kinds"/> (or any kind when null)
    /// and which carries <c>[Transpile]</c>. When
    /// <paramref name="typeName"/> is supplied, also requires
    /// <see cref="ISymbol.Name"/> match. Replaces three per-file
    /// reimplementations of the same loop.
    /// </summary>
    public static INamedTypeSymbol FindTranspilableType(
        CSharpCompilation compilation,
        IReadOnlyList<TypeKind>? kinds = null,
        string? typeName = null
    )
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (model.GetDeclaredSymbol(node) is not INamedTypeSymbol named)
                    continue;
                if (kinds is not null && !kinds.Contains(named.TypeKind))
                    continue;
                if (!Metano.Compiler.SymbolHelper.HasTranspile(named))
                    continue;
                if (typeName is not null && named.Name != typeName)
                    continue;
                return named;
            }
        }

        var kindLabel = kinds is null ? "type" : string.Join("/", kinds);
        var nameLabel = typeName is null ? "" : $" named '{typeName}'";
        throw new InvalidOperationException(
            $"No [Transpile]-annotated {kindLabel}{nameLabel} found in the source."
        );
    }

    /// <summary>
    /// Scans <paramref name="compilation"/> for the first declared
    /// <see cref="INamedTypeSymbol"/> whose name matches
    /// <paramref name="typeName"/> (or the first declared type
    /// when null). No <c>[Transpile]</c> filter — caller decides
    /// whether the test fixture needs the attribute. Replaces a
    /// per-file copy in <c>IrTypeSignatureHasherTests</c>.
    /// </summary>
    public static INamedTypeSymbol FindNamedType(
        CSharpCompilation compilation,
        string? typeName = null
    )
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (model.GetDeclaredSymbol(node) is not INamedTypeSymbol named)
                    continue;
                if (!visited.Add(named))
                    continue;
                if (typeName is null || named.Name == typeName)
                    return named;
            }
        }
        throw new InvalidOperationException($"Type '{typeName ?? "<first>"}' not found.");
    }
}
