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
        var type = FindTranspilableType(compilation, TypeKind.Enum);
        return IrEnumExtractor.Extract(type);
    }

    /// <summary>
    /// Compiles C# source, finds the first [Transpile]-annotated interface, and extracts it to IR.
    /// </summary>
    public static IrInterfaceDeclaration ExtractInterface(string csharpSource)
    {
        var compilation = Compile(csharpSource);
        var type = FindTranspilableType(compilation, TypeKind.Interface);
        return IrInterfaceExtractor.Extract(type);
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

    public static CSharpCompilation Compile(string csharpSource)
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
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
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

    private static INamedTypeSymbol FindTranspilableType(
        CSharpCompilation compilation,
        TypeKind kind
    )
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                var symbol = model.GetDeclaredSymbol(node);
                if (
                    symbol is INamedTypeSymbol namedType
                    && namedType.TypeKind == kind
                    && Metano.Compiler.SymbolHelper.HasTranspile(namedType)
                )
                {
                    return namedType;
                }
            }
        }

        throw new InvalidOperationException(
            $"No [Transpile]-annotated {kind} found in the source."
        );
    }
}
