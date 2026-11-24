using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Tests.TreeShaking;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests;

/// <summary>
/// Full emitted-output, <c>tsgo</c> type-checking, C# compile-time mutator
/// absence, overload resolution, and Dart <c>MS0051</c> diagnostics for
/// <c>ReadOnlyJsArray&lt;T&gt;</c>, <c>AsReadOnlyJsArray()</c>,
/// <c>AsJsArray()</c>, and <c>ToJsArray()</c> (spec 011 FR-004, FR-055,
/// FR-056, FR-056a; issue #737, W7-16).
/// </summary>
public class ReadOnlyListViewTranspileTests
{
    private const string ReadOnlyViewsSource = """
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Collections.ObjectModel;
        using Metano.Annotations;
        using Metano.Interop;

        [Transpile]
        public class ReadOnlyViewsHolder
        {
            private readonly ReadOnlyJsArray<int> _cached;

            public ReadOnlyViewsHolder(IReadOnlyList<int> items)
            {
                _cached = items.AsReadOnlyJsArray();
            }

            public ReadOnlyJsArray<int> ReturnFromList(List<int> items) => items.AsReadOnlyJsArray();

            public ReadOnlyJsArray<int> ReturnFromReadOnlyList(IReadOnlyList<int> items) =>
                items.AsReadOnlyJsArray();

            public ReadOnlyJsArray<int> ReturnFromReadOnlyCollection(
                ReadOnlyCollection<int> items
            ) => items.AsReadOnlyJsArray();

            public ReadOnlyJsArray<int> ReturnFromImmutableList(ImmutableList<int> items) =>
                items.AsReadOnlyJsArray();

            public ReadOnlyJsArray<int> ReturnFromImmutableArray(ImmutableArray<int> items) =>
                items.AsReadOnlyJsArray();

            public ReadOnlyJsArray<int> ReturnFromArrayCast(int[] items) =>
                ((IReadOnlyList<int>)items).AsReadOnlyJsArray();

            public ReadOnlyJsArray<int> ReturnStaticCall(IReadOnlyList<int> items) =>
                InteropExtensions.AsReadOnlyJsArray(items);

            public ReadOnlyJsArray<int> ReturnFromJsArrayImplicit(JsArray<int> items) => items;

            public ReadOnlyJsArray<int> ReturnFromArrayImplicit(int[] items) => items;

            public JsArray<int> CopyFromList(List<int> items) => items.ToJsArray();

            public JsArray<int> CopyFromReadOnlyList(IReadOnlyList<int> items) => items.ToJsArray();

            public JsArray<int> CopyStaticCall(IReadOnlyList<int> items) =>
                InteropExtensions.ToJsArray(items);

            public JsArray<int> WritableFromList(List<int> items) => items.AsJsArray();

            public int InspectParameter(ReadOnlyJsArray<int> items)
            {
                var sum = 0;

                foreach (var item in items)
                    sum += item;

                var mapped = items.Map(x => x * 2);
                var filtered = items.Filter(x => x > 0);
                var sliced = items.Slice(0, 1);
                var last = items[^1];
                var text = items.Join(",");
                var index = items.IndexOf(1);
                var has = items.Includes(1);

                return sum
                    + _cached.Length
                    + items.Length
                    + items[0]
                    + mapped.Length
                    + filtered.Length
                    + sliced.Length
                    + last
                    + text.Length
                    + index
                    + (has ? 1 : 0);
            }

            public string LastOrEmpty(ReadOnlyJsArray<string> items) => items.At(-1) ?? "";

            public ReadOnlyJsArray<ReadOnlyJsArray<int>> NestedReadOnly(
                ReadOnlyJsArray<ReadOnlyJsArray<int>> nested
            ) => nested;

            public ReadOnlyJsArray<int?>? NullableElements(ReadOnlyJsArray<int?>? items) => items;
        }
        """;

    [Test]
    public async Task ReadOnlyJsArray_ReturnFieldAndParameter_EmitExactTypeScriptAndPassTsgo()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ReadOnlyViewsSource);

        var errors = diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error).ToList();

        await Assert.That(errors).IsEmpty();

        const string expected = """
            import { ImmutableArray, ImmutableList, List, ReadOnlyList, asJsArray, asReadOnlyJsArray, fromEnd, type IReadOnlyList } from "metano-runtime/system/collections";

            export class ReadOnlyViewsHolder {
              private readonly _cached: readonly number[];

              constructor(items: IReadOnlyList<number>) {
                this._cached = asReadOnlyJsArray(items);
              }

              returnFromList(items: List<number>): readonly number[] {
                return asReadOnlyJsArray(items);
              }

              returnFromReadOnlyList(items: IReadOnlyList<number>): readonly number[] {
                return asReadOnlyJsArray(items);
              }

              returnFromReadOnlyCollection(items: ReadOnlyList<number>): readonly number[] {
                return asReadOnlyJsArray(items);
              }

              returnFromImmutableList(items: ImmutableList<number>): readonly number[] {
                return asReadOnlyJsArray(items);
              }

              returnFromImmutableArray(items: ImmutableArray<number>): readonly number[] {
                return asReadOnlyJsArray(items);
              }

              returnFromArrayCast(items: number[]): readonly number[] {
                return asReadOnlyJsArray(List.adopt<number>(items, { fixedSize: true }));
              }

              returnStaticCall(items: IReadOnlyList<number>): readonly number[] {
                return asReadOnlyJsArray(items);
              }

              returnFromJsArrayImplicit(items: number[]): readonly number[] {
                return items;
              }

              returnFromArrayImplicit(items: number[]): readonly number[] {
                return items;
              }

              copyFromList(items: List<number>): number[] {
                return items.toArray();
              }

              copyFromReadOnlyList(items: IReadOnlyList<number>): number[] {
                return items.toArray();
              }

              copyStaticCall(items: IReadOnlyList<number>): number[] {
                return items.toArray();
              }

              writableFromList(items: List<number>): number[] {
                return asJsArray(items);
              }

              inspectParameter(items: readonly number[]): number {
                let sum = 0;
                for (const item of items) {
                  sum = (sum + item) | 0;
                }
                const mapped = items.map((x: number) => Math.imul(x, 2));
                const filtered = items.filter((x: number) => x > 0);
                const sliced = items.slice(0, 1);
                const last = fromEnd(items, 1);
                const text = items.join(",");
                const index = items.indexOf(1);
                const has = items.includes(1);

                return (sum + this._cached.length + items.length + items[0]! + mapped.length + filtered.length + sliced.length + last + text.length + index + (has ? 1 : 0)) | 0;
              }

              lastOrEmpty(items: readonly string[]): string {
                return items.at(-1) ?? "";
              }

              nestedReadOnly(nested: readonly (readonly number[])[]): readonly (readonly number[])[] {
                return nested;
              }

              nullableElements(items: readonly (number | null)[] | null): readonly (number | null)[] | null {
                return items;
              }
            }

            """;

        await Assert.That(files["read-only-views-holder.ts"]).IsEqualTo(expected);

        var (exitCode, output) = await StrictTypeCheck.Run(new Dictionary<string, string>(), files);

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    [Arguments("arr[0] = 1;", "CS0200")]
    [Arguments("arr.Push(1);", "CS1061")]
    [Arguments("arr.Pop();", "CS1061")]
    [Arguments("arr.Shift();", "CS1061")]
    [Arguments("arr.Unshift(1);", "CS1061")]
    [Arguments("arr.Splice(0, 1);", "CS1061")]
    [Arguments("arr.Reverse();", "CS1061")]
    [Arguments("arr.Sort((a, b) => a - b);", "CS1061")]
    [Arguments("arr.AsList();", "CS1061")]
    public async Task ReadOnlyJsArray_DoesNotExposeMutatorsInCSharp(
        string statement,
        string expectedDiagnosticId
    )
    {
        var source = $$"""
            using Metano.Interop;

            public static class MutatorCheck
            {
                public static void Mutate(ReadOnlyJsArray<int> arr)
                {
                    {{statement}}
                }
            }
            """;

        var compilation = CompileSnippet(source);

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id)
            .ToList();

        await Assert.That(errors).Contains(expectedDiagnosticId);
    }

    [Test]
    public async Task AsJsArray_DoesNotBindOnIReadOnlyListInCSharp()
    {
        const string source = """
            using System.Collections.Generic;
            using Metano.Interop;

            public static class OverloadCheck
            {
                public static void Call(IReadOnlyList<int> items)
                {
                    items.AsJsArray();
                }
            }
            """;

        var compilation = CompileSnippet(source);

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();

        await Assert.That(errors).IsNotEmpty();

        await Assert.That(errors.All(e => e.StartsWith("CS", StringComparison.Ordinal))).IsTrue();

        await Assert.That(string.Join("\n", errors)).Contains("AsJsArray");
    }

    [Test]
    [Arguments("List<int> items", "items.AsJsArray()", "System.Collections.Generic.List<T>")]
    [Arguments(
        "List<int> items",
        "items.AsReadOnlyJsArray()",
        "System.Collections.Generic.List<T>"
    )]
    [Arguments("List<int> items", "items.ToJsArray()", "System.Collections.Generic.List<T>")]
    [Arguments(
        "IReadOnlyList<int> items",
        "items.AsReadOnlyJsArray()",
        "System.Collections.Generic.IReadOnlyList<T>"
    )]
    [Arguments(
        "IReadOnlyList<int> items",
        "items.ToJsArray()",
        "System.Collections.Generic.IReadOnlyList<T>"
    )]
    [Arguments(
        "ReadOnlyCollection<int> items",
        "items.AsReadOnlyJsArray()",
        "System.Collections.Generic.IReadOnlyList<T>"
    )]
    [Arguments(
        "ImmutableList<int> items",
        "items.AsReadOnlyJsArray()",
        "System.Collections.Generic.IReadOnlyList<T>"
    )]
    public async Task ExtensionOverloads_BindExpectedReceiverParameterType(
        string parameter,
        string call,
        string expectedOriginalParameterType
    )
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Collections.ObjectModel;
            using Metano.Interop;

            public static class BindingCheck
            {
                public static object Check({{parameter}}) => {{call}};
            }
            """;

        var compilation = CompileSnippet(source);

        var diagnostics = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        await Assert.That(diagnostics).IsEmpty();

        var tree = compilation.SyntaxTrees[0];
        var model = compilation.GetSemanticModel(tree);

        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();

        var symbol = (IMethodSymbol?)model.GetSymbolInfo(invocation).Symbol;
        var originalMethod = symbol?.ReducedFrom ?? symbol;

        await Assert.That(originalMethod).IsNotNull();

        await Assert
            .That(originalMethod!.Parameters[0].OriginalDefinition.Type.ToDisplayString())
            .IsEqualTo(expectedOriginalParameterType);
    }

    [Test]
    [Arguments(
        "public static int Count(IReadOnlyList<int> items) => items.AsReadOnlyJsArray().Length;"
    )]
    [Arguments("public static int Count(List<int> items) => items.AsReadOnlyJsArray().Length;")]
    [Arguments("public static int Count(IReadOnlyList<int> items) => items.ToJsArray().Length;")]
    [Arguments("public static ReadOnlyJsArray<int> Echo(ReadOnlyJsArray<int> items) => items;")]
    [Arguments(
        "[Transpile] public class Holder { private readonly ReadOnlyJsArray<int> _items = []; }"
    )]
    public async Task ReadOnlyJsArray_AndViews_InDart_ReportTargetSpecificInterop(string member)
    {
        var diagnostics = DartFrontendDiagnostics(
            $$"""
            using System.Collections.Generic;
            using Metano.Interop;

            [Transpile]
            public static class Views
            {
                {{member}}
            }
            """
        );

        var ms0051 = diagnostics
            .Where(d => d.Code == DiagnosticCodes.TargetSpecificInterop)
            .ToList();

        await Assert.That(ms0051).IsNotEmpty();
    }

    private static CSharpCompilation CompileSnippet(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview)
        );

        return CSharpCompilation.Create(
            "ReadOnlyListViewSnippetAssembly",
            [tree],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
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
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        return new CSharpSourceFrontend()
            .ExtractFromCompilation(compilation, Metano.Annotations.TargetLanguage.Dart)
            .Diagnostics;
    }
}
