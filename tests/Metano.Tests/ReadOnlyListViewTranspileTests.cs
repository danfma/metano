using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// Lowering and diagnostics for <c>AsJsArray()</c> across <c>List&lt;T&gt;</c> and
/// <c>IReadOnlyList&lt;T&gt;</c> (spec 011 FR-056, issue #737).
/// </summary>
public class ReadOnlyListViewTranspileTests
{
    [Test]
    [Arguments("List<int> items", "items.AsJsArray()", "asJsArray(items)")]
    [Arguments("IReadOnlyList<int> items", "items.AsJsArray()", "asJsArray(items)")]
    [Arguments("ReadOnlyCollection<int> items", "items.AsJsArray()", "asJsArray(items)")]
    [Arguments("ImmutableList<int> items", "items.AsJsArray()", "asJsArray(items)")]
    [Arguments("ImmutableArray<int> items", "items.AsJsArray()", "asJsArray(items)")]
    [Arguments(
        "int[] items",
        "((IReadOnlyList<int>)items).AsJsArray()",
        "asJsArray(List.adopt<number>(items, { fixedSize: true }))"
    )]
    [Arguments(
        "IReadOnlyList<int> items",
        "InteropExtensions.AsJsArray(items)",
        "asJsArray(items)"
    )]
    public async Task AsJsArray_OnListOrReadOnlyList_LowersToAsJsArrayHelper(
        string parameter,
        string call,
        string expectedExpression
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Collections.ObjectModel;
            using Metano.Annotations;
            using Metano.Interop;

            [Transpile]
            public static class Views
            {
                public static JsArray<int> View({{parameter}}) => {{call}};
            }
            """
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(unsupported).IsEmpty();
        await Assert.That(files["views.ts"]).Contains($"return {expectedExpression};");
        await Assert.That(files["views.ts"]).Contains("asJsArray");
        await Assert.That(files["views.ts"]).Contains("from \"metano-runtime/system/collections\"");
    }

    [Test]
    public async Task AsJsArray_OnIReadOnlyList_InDart_ReportsTargetSpecificInterop()
    {
        var diagnostics = DartFrontendDiagnostics(
            """
            using System.Collections.Generic;
            using Metano.Interop;

            [Transpile]
            public static class Views
            {
                public static int Count(IReadOnlyList<int> items) => items.AsJsArray().Length;
            }
            """
        );

        var ms0051 = diagnostics
            .Where(d => d.Code == DiagnosticCodes.TargetSpecificInterop)
            .ToList();

        await Assert.That(ms0051).IsNotEmpty();
    }

    private static IReadOnlyList<MetanoDiagnostic> DartFrontendDiagnostics(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            "using Metano.Annotations;\n" + source,
            new CSharpParseOptions(LanguageVersion.Preview)
        );

        var compilation = CSharpCompilation.Create(
            "DartReadOnlyListViewTestAssembly",
            [tree],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
        );

        return new CSharpSourceFrontend()
            .ExtractFromCompilation(compilation, Metano.Annotations.TargetLanguage.Dart)
            .Diagnostics;
    }
}
