using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.TypeScript;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Tests.IR;

/// <summary>
/// Exercises <see cref="IrToTsOverloadDispatcherBridge"/> end-to-end:
/// compiles a C# class with overloads, runs the IR extractor to gather an
/// <see cref="IrMethodDeclaration"/> whose <c>Overloads</c> carries the
/// siblings, feeds it through the bridge, and prints the resulting members
/// to pin the dispatcher shape.
/// </summary>
public class IrToTsOverloadDispatcherBridgeTests
{
    [Test]
    public async Task TwoOverloads_EmitsDispatcherAndFastPaths()
    {
        var (primary, containingTypeName) = ExtractOverloadGroup(
            """
            public class Adder
            {
                public int Sum(int a, int b) => a + b;
                public int Sum(int a) => a;
            }
            """,
            methodName: "Sum"
        );

        var members = IrToTsOverloadDispatcherBridge.BuildMethod(primary, containingTypeName);

        var printed = PrintMembers(members, containingTypeName);
        // Public dispatcher with ...args: unknown[].
        await Assert.That(printed).Contains("sum(...args: unknown[])");
        // Two private fast paths — one per overload. Fast-path names use the
        // overload's parameter names (legacy shape) so the generated code is
        // human-readable.
        await Assert.That(printed).Contains("private sumAB(a: number, b: number)");
        await Assert.That(printed).Contains("private sumA(a: number)");
        // Dispatcher branches on argument count and uses the metano-runtime
        // type-check helpers legacy emits, so same-arity numeric overloads
        // remain distinguishable at runtime.
        await Assert.That(printed).Contains("args.length === 2");
        await Assert.That(printed).Contains("args.length === 1");
        await Assert.That(printed).Contains("isInt32(args[0])");
        // Unmatched overload falls through to a throw.
        await Assert.That(printed).Contains("throw new Error");
    }

    [Test]
    public async Task SameArityNumericOverloads_UseDistinctRuntimeGuards()
    {
        // Without distinct guards the first arity-matching branch always wins,
        // so M(int)/M(double) would become indistinguishable at runtime. The
        // legacy dispatcher used isInt32/isFloat64; the IR bridge must too.
        var (primary, containingTypeName) = ExtractOverloadGroup(
            """
            public class NumericDispatch
            {
                public int Compute(int value) => value;
                public double Compute(double value) => value;
            }
            """,
            methodName: "Compute"
        );

        var printed = PrintMembers(
            IrToTsOverloadDispatcherBridge.BuildMethod(primary, containingTypeName),
            containingTypeName
        );
        await Assert.That(printed).Contains("isInt32(args[0])");
        await Assert.That(printed).Contains("isFloat64(args[0])");
    }

    [Test]
    public async Task DecimalOverload_UsesIsFloat64_NotInstanceofDecimal()
    {
        // IrToTsTypeMapper still lowers `decimal` to `number`, so the guard
        // must accept plain numbers. `instanceof Decimal` would never match
        // and also introduce a runtime reference the legacy didn't require.
        var (primary, containingTypeName) = ExtractOverloadGroup(
            """
            public class MoneyDispatch
            {
                public int Pay(int cents) => cents;
                public decimal Pay(decimal amount) => amount;
            }
            """,
            methodName: "Pay"
        );

        var printed = PrintMembers(
            IrToTsOverloadDispatcherBridge.BuildMethod(primary, containingTypeName),
            containingTypeName
        );
        await Assert.That(printed).Contains("isFloat64(args[0])");
        await Assert.That(printed).DoesNotContain("instanceof Decimal");
    }

    [Test]
    public async Task GenericOverload_CarriesTypeParametersOnFastPath()
    {
        // The fast-path method for a generic overload like Identity<T>(T value)
        // must declare <T> or it references an undeclared type parameter in
        // its signature and the emitted TS fails to compile.
        var (primary, containingTypeName) = ExtractOverloadGroup(
            """
            public class Generics
            {
                public int Identity(int value) => value;
                public T Identity<T>(T value) => value;
            }
            """,
            methodName: "Identity"
        );

        var printed = PrintMembers(
            IrToTsOverloadDispatcherBridge.BuildMethod(primary, containingTypeName),
            containingTypeName
        );
        // Both overloads share the same parameter name (`value`), so the bridge
        // falls back to type-based naming. The type parameter falls through
        // SimpleTypeName to the "Value" fallback, so the generic fast path is
        // still distinguishable and declares its type parameter.
        await Assert.That(printed).Contains("identityValue<T>(value: T): T");
    }

    [Test]
    public async Task StaticOverloads_DispatchViaClassName()
    {
        var (primary, containingTypeName) = ExtractOverloadGroup(
            """
            public class MathUtils
            {
                public static int Abs(int x) => x < 0 ? -x : x;
                public static double Abs(double x) => x < 0 ? -x : x;
            }
            """,
            methodName: "Abs"
        );

        var members = IrToTsOverloadDispatcherBridge.BuildMethod(primary, containingTypeName);
        var printed = PrintMembers(members, containingTypeName);
        await Assert.That(printed).Contains("static abs(...args: unknown[])");
        // Static overloads delegate through the class name, not `this`. Both
        // overloads share the parameter name `x`, so the bridge falls back to
        // type-based suffixes — matching the legacy dispatcher.
        await Assert.That(printed).Contains("MathUtils.absInt(");
        await Assert.That(printed).Contains("MathUtils.absDouble(");
    }

    [Test]
    public async Task StringEnumOverload_UsesExhaustiveValueCheck()
    {
        // A string-enum parameter must be guarded by an exhaustive value check
        // (`=== "a" || === "b"`), not an `instanceof` — string enums lower to
        // a TS union of literals and have no class identity at runtime.
        var (primary, containingTypeName) = ExtractOverloadGroup(
            """
            using Metano.Annotations;

            [StringEnum]
            public enum Priority { Low, Medium, High }

            public class Flagger
            {
                public void Flag(int id) { }
                public void Flag(Priority p) { }
            }
            """,
            methodName: "Flag"
        );

        var printed = PrintMembers(
            IrToTsOverloadDispatcherBridge.BuildMethod(primary, containingTypeName),
            containingTypeName
        );
        await Assert.That(printed).Contains("args[0] === \"Low\"");
        await Assert.That(printed).Contains("args[0] === \"Medium\"");
        await Assert.That(printed).Contains("args[0] === \"High\"");
        await Assert.That(printed).DoesNotContain("instanceof Priority");
    }

    [Test]
    public async Task NumericEnumOverload_UsesIsInt32Check()
    {
        // Without [StringEnum] the enum lowers to a TS numeric-backed const —
        // the right runtime test is isInt32, not instanceof.
        var (primary, containingTypeName) = ExtractOverloadGroup(
            """
            public enum Status { Pending, Done }

            public class Toggler
            {
                public void Toggle(string name) { }
                public void Toggle(Status s) { }
            }
            """,
            methodName: "Toggle"
        );

        var printed = PrintMembers(
            IrToTsOverloadDispatcherBridge.BuildMethod(primary, containingTypeName),
            containingTypeName
        );
        await Assert.That(printed).Contains("isInt32(args[0])");
        await Assert.That(printed).DoesNotContain("instanceof Status");
    }

    [Test]
    public async Task InterfaceOverload_UsesTypeofObjectCheck()
    {
        // Interfaces are erased at the TS level — `instanceof IFoo` would
        // reference an undefined identifier, so the bridge falls back to the
        // structural typeof check the legacy dispatcher emits.
        var (primary, containingTypeName) = ExtractOverloadGroup(
            """
            public interface IShape { int Area(); }

            public class Drawer
            {
                public void Draw(int n) { }
                public void Draw(IShape shape) { }
            }
            """,
            methodName: "Draw"
        );

        var printed = PrintMembers(
            IrToTsOverloadDispatcherBridge.BuildMethod(primary, containingTypeName),
            containingTypeName
        );
        await Assert.That(printed).Contains("typeof args[0] === \"object\"");
        await Assert.That(printed).DoesNotContain("instanceof IShape");
    }

    [Test]
    public async Task SingleMethod_ReturnsEmptyList()
    {
        // No overloads → the bridge returns an empty list so the caller
        // picks the regular single-method path.
        var (primary, containingTypeName) = ExtractOverloadGroup(
            """
            public class Adder
            {
                public int Sum(int a, int b) => a + b;
            }
            """,
            methodName: "Sum"
        );
        var members = IrToTsOverloadDispatcherBridge.BuildMethod(primary, containingTypeName);
        await Assert.That(members).IsEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles <paramref name="classSource"/> (wrapped in the standard
    /// usings) and extracts the method group for <paramref name="methodName"/>,
    /// returning a primary <see cref="IrMethodDeclaration"/> whose Overloads
    /// carries the siblings — exactly the shape IrToTsClassEmitter hands
    /// to the bridge.
    /// </summary>
    private static (IrMethodDeclaration Primary, string ContainingType) ExtractOverloadGroup(
        string classSource,
        string methodName
    )
    {
        var compilation = IrTestHelper.Compile(classSource);
        var tree = compilation.SyntaxTrees.First();
        var typeDecl = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var model = compilation.GetSemanticModel(tree);
        var typeSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(typeDecl)!;

        var overloads = typeSymbol
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .OrderByDescending(m => m.Parameters.Length)
            .Select(m => IrMethodExtractor.Extract(m, originResolver: null, compilation))
            .ToList();
        var primary = overloads[0] with { Overloads = overloads.Skip(1).ToList() };
        return (primary, typeSymbol.Name);
    }

    private static string PrintMembers(IReadOnlyList<TsClassMember> members, string className)
    {
        // Wrap the members in a synthetic class so the printer renders them
        // in context. Strip the class wrapping afterwards to keep assertions
        // terse.
        var cls = new TsClass(className, Constructor: null, Members: members);
        var file = new TsSourceFile("dispatcher.ts", [cls]);
        var printed = new Printer().Print(file);
        var start = printed.IndexOf('\n') + 1;
        var end = printed.LastIndexOf('}');
        return printed[start..end].Trim();
    }
}
