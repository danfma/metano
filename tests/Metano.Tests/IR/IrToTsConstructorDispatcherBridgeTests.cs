using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.TypeScript;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests.IR;

/// <summary>
/// Exercises <see cref="IrToTsConstructorDispatcherBridge"/> end-to-end:
/// compiles a C# class with multiple constructors, extracts the
/// <see cref="IrConstructorDeclaration"/> whose <c>Overloads</c> carries
/// the siblings, feeds it through the bridge, and pins the emitted TS
/// constructor shape.
/// </summary>
public class IrToTsConstructorDispatcherBridgeTests
{
    [Test]
    public async Task TwoConstructors_EmitsOverloadSignaturesAndGuardedBody()
    {
        var ctor = Extract(
            """
            public class Box
            {
                private int value;
                public Box(int v) { value = v; }
                public Box(string s) { value = s.Length; }
            }
            """
        );

        var printed = PrintConstructor(IrToTsConstructorDispatcherBridge.Build(ctor)!);
        // One overload signature per constructor, most-specific first (same arity
        // here, so order is preserved by the input). The TS printer tags every
        // ctor parameter with `public` — we assert on the parameter shape alone.
        await Assert.That(printed).Contains("v: number);");
        await Assert.That(printed).Contains("s: string);");
        // Dispatcher takes `...args` and branches on arity + runtime type checks.
        await Assert.That(printed).Contains("...args: unknown[]");
        await Assert.That(printed).Contains("args.length === 1 && isInt32(args[0])");
        await Assert.That(printed).Contains("args.length === 1 && isString(args[0])");
        // Unmatched — throw.
        await Assert.That(printed).Contains("throw new Error(\"No matching constructor\")");
    }

    [Test]
    public async Task ConstructorWithBaseCall_EmitsSuperInsideBranch()
    {
        var ctor = Extract(
            """
            public class Base
            {
                protected Base(int x) { }
            }
            public class Derived : Base
            {
                public Derived(int x) : base(x) { }
                public Derived() : base(0) { }
            }
            """,
            typeName: "Derived"
        );

        var printed = PrintConstructor(IrToTsConstructorDispatcherBridge.Build(ctor)!);
        // Each branch opens with the matching super(...) call derived from the
        // C# `: base(...)` initializer.
        await Assert.That(printed).Contains("super(x)");
        await Assert.That(printed).Contains("super(0)");
    }

    [Test]
    public async Task SingleConstructor_ReturnsNull()
    {
        // No dispatcher needed when the class has a single constructor — the
        // caller picks the regular single-ctor path in that case.
        var ctor = Extract(
            """
            public class Solo
            {
                public Solo(int v) { }
            }
            """
        );
        await Assert.That(IrToTsConstructorDispatcherBridge.Build(ctor)).IsNull();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static IrConstructorDeclaration Extract(string source, string typeName = "")
    {
        var compilation = IrTestHelper.Compile(source);
        var tree = compilation.SyntaxTrees.First();
        var typeDecls = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
        var typeDecl =
            typeName.Length == 0
                ? typeDecls.Last()
                : typeDecls.First(t => t.Identifier.ValueText == typeName);
        var model = compilation.GetSemanticModel(tree);
        var typeSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(typeDecl)!;
        return IrConstructorExtractor.Extract(typeSymbol, compilation: compilation)!;
    }

    private static string PrintConstructor(TsConstructor ctor)
    {
        var cls = new TsClass("Box", Constructor: ctor, Members: []);
        var file = new TsSourceFile("ctor.ts", [cls]);
        return new Printer().Print(file);
    }
}
