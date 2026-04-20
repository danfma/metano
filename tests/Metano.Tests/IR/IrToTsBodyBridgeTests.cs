using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests.IR;

/// <summary>
/// Exercises the IR → TypeScript body bridges by extracting a C# method, mapping
/// its IR body to TS AST, and printing the resulting method member. Pins the
/// expected output so we notice drift before wiring the bridge into the pipeline.
/// </summary>
public class IrToTsBodyBridgeTests
{
    [Test]
    public async Task ExpressionBodiedReturn_LowersToArrowReturn()
    {
        var ts = PrintMethodFromIr("int Add() => 1 + 2;");
        await Assert.That(ts).Contains("add(): number").And.Contains("return 1 + 2;");
    }

    [Test]
    public async Task AssignmentAndCall_PrintAsStatements()
    {
        var ts = PrintMethodFromIr(
            """
            void Update() {
                var x = 1;
                doSomething(x);
            }
            """,
            extraMembers: "void doSomething(int v) { }"
        );
        // `x` is declared once and never reassigned, so the extractor's
        // effective-const pass promotes it to `const` — matching the legacy
        // ExpressionTransformer behavior on the same input.
        await Assert
            .That(ts)
            .Contains("update()")
            .And.Contains("const x = 1;")
            .And.Contains("doSomething(x);");
    }

    [Test]
    public async Task IfWithThenOnly_ProducesIfStatement()
    {
        var ts = PrintMethodFromIr(
            """
            void Check(bool flag) {
                if (flag) return;
            }
            """
        );
        await Assert.That(ts).Contains("if (flag) {").And.Contains("return;");
    }

    [Test]
    public async Task BclMapping_ListAdd_LowersToPushWhenRegistryProvided()
    {
        var registry = DeclarativeMappingRegistry.CreateForTests(
            new Dictionary<(string, string), DeclarativeMappingEntry>
            {
                {
                    ("System.Collections.Generic.List<T>", "Add"),
                    new DeclarativeMappingEntry(JsName: "push", JsTemplate: null)
                },
            }
        );

        var ts = PrintMethodFromIr(
            """
            void Append(System.Collections.Generic.List<int> xs, int v) {
                xs.Add(v);
            }
            """,
            bclRegistry: registry
        );

        await Assert.That(ts).Contains("xs.push(v);");
    }

    [Test]
    public async Task IsNullPattern_LowersToStrictEquality()
    {
        var ts = PrintMethodFromIr("bool IsNull(object? x) => x is null;");
        await Assert.That(ts).Contains("x === null");
    }

    [Test]
    public async Task IsTypePattern_LowersToInstanceofWhenNamed()
    {
        // The nested `Foo` resolves to `T.Foo` (its fully-qualified nested name),
        // which is exactly what the TS backend needs to render an instanceof test.
        var ts = PrintMethodFromIr(
            "bool IsFoo(object o) => o is Foo;",
            extraMembers: "public class Foo { }"
        );
        await Assert.That(ts).Contains("instanceof T.Foo");
    }

    [Test]
    public async Task Lambda_LowersToArrowFunction()
    {
        // The TS printer collapses a single-return arrow body to expression form:
        // (x) => x + 1 rather than (x) => { return x + 1; }.
        var ts = PrintMethodFromIr("System.Func<int, int> Make() => x => x + 1;");
        await Assert.That(ts).Contains("(x: number) => x + 1");
    }

    [Test]
    public async Task StringInterpolation_LowersToTemplateLiteral()
    {
        var ts = PrintMethodFromIr("""string Greet(string name) => $"hello {name}!";""");
        await Assert.That(ts).Contains("`hello ${name}!`");
    }

    [Test]
    public async Task BclMapping_WithoutRegistry_EmitsRawCall()
    {
        // Same source as the previous test, but without a registry — the bridge
        // falls back to the raw `xs.add(v)` form (camelCased member name).
        var ts = PrintMethodFromIr(
            """
            void Append(System.Collections.Generic.List<int> xs, int v) {
                xs.Add(v);
            }
            """
        );

        await Assert.That(ts).Contains("xs.add(v);");
    }

    [Test]
    public async Task RecordLikeBuilder_ParityWithHandWritten()
    {
        // Mirrors SampleCounter.Counter.Increment: an expression-bodied method
        // that builds a new instance using a binary expression. The test class is
        // named `T` (see helper) so the method returns a `T`.
        var ts = PrintMethodFromIr(
            "T Rebuild() => new T(count + 1);",
            extraMembers: """
            public int count;
            public T(int count) { this.count = count; }
            """
        );
        // The reference to `count` is an implicit-`this` access to the instance
        // field, which the IR extractor now expands to `this.count`.
        await Assert.That(ts).Contains("new T(this.count + 1)");
    }

    [Test]
    public async Task PropertyPattern_LowersToConjunctionOfSubtests()
    {
        // `obj is Point { X: 0 }` should become
        // `obj instanceof Point && obj.x === 0` — the type filter plus a
        // chain of camelCased member comparisons. Without the type filter
        // (`obj is { X: 0 }`) the bridge drops the `instanceof` check and
        // emits only the sub-tests.
        var ts = PrintMethodFromIr(
            "bool Check(object obj) => obj is Point { X: 0 };",
            extraMembers: "public record Point(int X, int Y);"
        );
        // Point is nested inside the test host `T`, so the mapped type keeps
        // the `T.` qualifier — that's the IR type ref lowering, not the
        // property-pattern layer.
        await Assert.That(ts).Contains("obj instanceof T.Point");
        await Assert.That(ts).Contains("obj.x === 0");
    }

    [Test]
    public async Task ListPattern_LowersToLengthGatedIndexChecks()
    {
        // `arr is [1, 2]` → `arr.length === 2 && arr[0] === 1 && arr[1] === 2`.
        // A length check gates the match so mismatched arities short-circuit.
        var ts = PrintMethodFromIr("bool Match(int[] arr) => arr is [1, 2];");
        await Assert.That(ts).Contains("arr.length === 2");
        await Assert.That(ts).Contains("arr[0] === 1");
        await Assert.That(ts).Contains("arr[1] === 2");
    }

    [Test]
    public async Task ListPatternWithSlice_UsesLengthMinimumAndTailReverseIndex()
    {
        // `arr is [1, .., 9]` keeps `>=` for the length gate and reverse-
        // indexes the trailing element via `arr.length - 1`.
        var ts = PrintMethodFromIr("bool Match(int[] arr) => arr is [1, .., 9];");
        await Assert.That(ts).Contains("arr.length >= 2");
        await Assert.That(ts).Contains("arr[0] === 1");
        await Assert.That(ts).Contains("arr[arr.length - 1] === 9");
    }

    [Test]
    public async Task PositionalPattern_LowersToIndexedConjunction()
    {
        // `pair is (1, 2)` decomposes into index accesses on the scrutinee.
        // Tuples in this pipeline are represented as indexable values, so
        // `pair[0] === 1 && pair[1] === 2` matches the shape.
        var ts = PrintMethodFromIr("bool Match((int, int) pair) => pair is (1, 2);");
        await Assert.That(ts).Contains("pair[0] === 1");
        await Assert.That(ts).Contains("pair[1] === 2");
    }

    [Test]
    public async Task RelationalPattern_LowersToBinaryComparison()
    {
        // `x is > 0` → `x > 0`. The IR extracts a IrRelationalPattern whose
        // Operator carries the comparison and the bridge emits the TS form.
        var ts = PrintMethodFromIr("bool Positive(int x) => x is > 0;");
        await Assert.That(ts).Contains("x > 0");
    }

    [Test]
    public async Task LogicalAndPattern_LowersToDoubleAmpersand()
    {
        var ts = PrintMethodFromIr("bool Between(int x) => x is > 0 and <= 10;");
        await Assert.That(ts).Contains("x > 0 && x <= 10");
    }

    [Test]
    public async Task NotPattern_LowersToNegation()
    {
        var ts = PrintMethodFromIr("bool NotNull(object? x) => x is not null;");
        await Assert.That(ts).Contains("!(x === null)");
    }

    [Test]
    public async Task EmitTemplateOnInstanceMethod_ExpandsInline()
    {
        // `[Emit("$0.toFixed($1)")] int Foo(int digits)` — the extractor
        // synthesizes the receiver as the first template argument, so a
        // call `value.Foo(2)` lowers to `value.toFixed(2)` via the TS
        // template expander.
        var ts = PrintMethodFromIr(
            """
            string Format(Wrapper value) => value.Foo(2);
            """,
            extraMembers: """
            public class Wrapper
            {
                [Emit("$0.toFixed($1)")]
                public string Foo(int digits) => throw null!;
            }
            """
        );
        await Assert.That(ts).Contains(".toFixed(");
    }

    [Test]
    public async Task EmitTemplateOnStaticMethod_ExpandsArgumentsOnly()
    {
        // For static `[Emit]` methods Roslyn exposes the call without a
        // receiver, so the template sees only the explicit arguments.
        var ts = PrintMethodFromIr(
            """
            string Format(int x) => Wrapper.Hex(x);
            """,
            extraMembers: """
            public static class Wrapper
            {
                [Emit("($0).toString(16)")]
                public static string Hex(int v) => throw null!;
            }
            """
        );
        await Assert.That(ts).Contains("(x).toString(16)");
    }

    [Test]
    public async Task LambdaWithNoEmitReceiverType_OmitsTypeAnnotation()
    {
        // Ambient `[NoEmit]` parameter types have no TS identifier to emit —
        // the bridge drops the annotation so the lambda's inferred type
        // comes from the call-site signature at consumption time. Matches
        // the legacy LambdaHandler behavior.
        var ts = PrintMethodFromIr(
            "void Use(System.Action<IAmbient> cb) { cb(default!); cb2(x => x.Read()); }",
            extraMembers: """
            [NoEmit]
            public interface IAmbient { int Read(); }
            public void cb2(System.Action<IAmbient> cb) { }
            """
        );
        await Assert.That(ts).Contains("(x) => x.read()");
    }

    [Test]
    public async Task InlineWrapperNew_LowersToCreateFactory()
    {
        // `new UserId(v)` on an [InlineWrapper] struct → `UserId.create(v)`.
        // The discriminator is IrNamedTypeSemantics.Kind = InlineWrapper,
        // filled at extraction time from the [InlineWrapper] attribute.
        var ts = PrintMethodFromIr(
            "UserId Wrap(string s) => new UserId(s);",
            extraMembers: """
            [InlineWrapper]
            public readonly struct UserId
            {
                public UserId(string value) { Value = value; }
                public string Value { get; }
            }
            """
        );
        await Assert.That(ts).Contains("UserId.create(s)");
        await Assert.That(ts).DoesNotContain("new UserId(");
    }

    [Test]
    public async Task SwitchExpression_WithDiscardFallback_LowersToTernaryChain()
    {
        // When the switch ends with a bare `_ => value` arm the bridge
        // collapses into a nested ternary — matching the legacy output
        // byte-for-byte, inline with the scrutinee so the read flows
        // naturally.
        var ts = PrintMethodFromIr("int Classify(int n) => n switch { 0 => 10, _ => 20 };");
        await Assert.That(ts).Contains("n === 0 ? 10 : 20");
    }

    [Test]
    public async Task SwitchExpression_WithoutDiscardFallback_LowersToIifeWithThrow()
    {
        // No catch-all → C#'s switch is non-exhaustive and throws at runtime.
        // The bridge preserves that semantics by emitting an IIFE whose
        // trailing statement throws on no match.
        var ts = PrintMethodFromIr("int Classify(int n) => n switch { 0 => 10, 1 => 20 };");
        await Assert.That(ts).Contains("(($s: any) => {");
        await Assert.That(ts).Contains("if ($s === 0)");
        await Assert.That(ts).Contains("return 10;");
        await Assert.That(ts).Contains("throw new Error(\"Non-exhaustive switch expression\")");
    }

    // -- helpers --

    /// <summary>
    /// Compiles the C# snippet, runs IrStatementExtractor over the target method,
    /// then maps the IR body to TS AST and prints it as a TsMethodMember.
    /// </summary>
    private static string PrintMethodFromIr(
        string method,
        string? extraMembers = null,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        var csharp = $$"""
            public class T
            {
                {{extraMembers ?? ""}}
                {{method}}
            }
            """;
        var compilation = IrTestHelper.Compile(csharp);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var methodSyntax = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Last();

        var methodSymbol = (IMethodSymbol)model.GetDeclaredSymbol(methodSyntax)!;
        var stmtExtractor = new IrStatementExtractor(model);
        var irBody = stmtExtractor.ExtractBody(
            methodSyntax.Body,
            methodSyntax.ExpressionBody,
            methodSymbol.ReturnsVoid
        );

        var tsBody = IrToTsStatementBridge.MapBody(irBody, bclRegistry);

        var methodMember = new TsMethodMember(
            methodSymbol.Name[0..1].ToLowerInvariant() + methodSymbol.Name[1..],
            methodSymbol.Parameters.Select(p => new TsParameter(p.Name, new TsAnyType())).ToList(),
            ReturnType: methodSymbol.ReturnsVoid ? new TsVoidType() : new TsNamedType("number"),
            Body: tsBody
        );

        var cls = new TsClass("T", Constructor: null, Members: [methodMember]);
        var file = new TsSourceFile("test.ts", [cls]);
        var printed = new Printer().Print(file);

        // Strip the class wrapping to keep assertions terse.
        var start = printed.IndexOf('\n') + 1;
        var end = printed.LastIndexOf('}');
        return printed[start..end].Trim();
    }
}
