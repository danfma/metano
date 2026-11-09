using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Tests.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// The core naming policy of spec 013 slice 013a (FR-001–FR-003, FR-006, FR-024;
/// contracts/naming.md § Stage 1 and § Spellings): the suffixes the class
/// extractor records on each overload of a dispatched group, in declaration order.
/// </summary>
public class OverloadNamePolicyTests
{
    [Test]
    public async Task DistinctParameterNames_NameTheGroup_WithNoJoiner()
    {
        var suffixes = SuffixesOf(
            """
            public int Add(int width, int height) => 0;
            public int Add(int size) => 0;
            """,
            "Add"
        );

        await Assert.That(suffixes).IsEqualTo("WidthHeight / Size");
    }

    [Test]
    public async Task ParameterlessOverload_IsDefault_InEveryPass()
    {
        var byName = SuffixesOf(
            """
            public int Now() => 0;
            public int Now(int tick) => tick;
            """,
            "Now"
        );

        var byType = SuffixesOf(
            """
            public int Now() => 0;
            public int Now(int a) => a;
            public int Now(string a) => 0;
            """,
            "Now"
        );

        await Assert.That(byName).IsEqualTo("Default / Tick");
        await Assert.That(byType).IsEqualTo("Default / Int32 / String");
    }

    /// <summary>
    /// FR-005: a <c>[Name]</c> pins its overload's suffix in every pass — the
    /// others are named by the normal rules, and a collision with the pin
    /// moves them, never the pin.
    /// </summary>
    [Test]
    public async Task NamePin_ReplacesTheSuffix_InEveryPass()
    {
        var byName = SuffixesOf(
            """
            public int Add(int width, int height) => 0;
            [Name("cents")]
            public int Add(int value) => 0;
            """,
            "Add"
        );

        var byType = SuffixesOf(
            """
            public int Add(int x) => 0;
            [Name("x")]
            public int Add(string s) => 0;
            public int Add(bool x) => 0;
            """,
            "Add"
        );

        await Assert.That(byName).IsEqualTo("WidthHeight / Cents");
        await Assert.That(byType).IsEqualTo("Int32 / X / Boolean");
    }

    [Test]
    public async Task PrimitiveSpellings_AreTheExactClrNames()
    {
        var suffixes = SuffixesOf(
            """
            public void P(sbyte a) { }
            public void P(byte a) { }
            public void P(short a) { }
            public void P(ushort a) { }
            public void P(int a) { }
            public void P(uint a) { }
            public void P(long a) { }
            public void P(ulong a) { }
            public void P(nint a) { }
            public void P(nuint a) { }
            public void P(float a) { }
            public void P(double a) { }
            public void P(decimal a) { }
            public void P(bool a) { }
            public void P(char a) { }
            public void P(string a) { }
            public void P(object a) { }
            """,
            "P"
        );

        await Assert
            .That(suffixes)
            .IsEqualTo(
                "SByte / Byte / Int16 / UInt16 / Int32 / UInt32 / Int64 / UInt64 / IntPtr / UIntPtr / Single / Double / Decimal / Boolean / Char / String / Object"
            );
    }

    [Test]
    public async Task CompositeSpellings_FollowTheSpellingTable()
    {
        var suffixes = SuffixesOf(
            """
            public void C(List<int> a) { }
            public void C(int[] a) { }
            public void C(int[][] a) { }
            public void C(int? a) { }
            public void C(string? a) { }
            public void C(Dictionary<string, int> a) { }
            public void C(Func<int, bool> a) { }
            public void C((int, string) a) { }
            public void C(params long[] a) { }
            public void C(out int a) { a = 0; }
            public void C<T>(T a) { }
            """,
            "C"
        );

        await Assert
            .That(suffixes)
            .IsEqualTo(
                "ListOfInt32 / Int32Array / Int32ArrayArray / Int32OrNull / StringOrNull / DictionaryOfStringInt32 / FuncOfInt32Boolean / TupleOfInt32String / Int64Array / OutOfInt32 / T"
            );
    }

    [Test]
    public async Task Climb_QualifiesOnlyTheCollidingOverloads()
    {
        var suffixes = SuffixesOf(
            """
            public void P(N1.Foo a) { }
            public void P(N2.Foo a) { }
            public void P(int a) { }
            """,
            "P",
            """
            namespace N1 { public class Foo { } }
            namespace N2 { public class Foo { } }
            """
        );

        await Assert.That(suffixes).IsEqualTo("N1Foo / N2Foo / Int32");
    }

    [Test]
    public async Task Climb_WalksContainingTypesThenNamespacesThenGlobal()
    {
        var nested = SuffixesOf(
            """
            public void N(Outer.Inner a) { }
            public void N(Other.Inner a) { }
            """,
            "N",
            """
            public class Outer { public class Inner { } }
            public class Other { public class Inner { } }
            """
        );

        var deep = SuffixesOf(
            """
            public void D(X.A.B.Foo a) { }
            public void D(Y.A.B.Foo a) { }
            """,
            "D",
            """
            namespace X.A.B { public class Foo { } }
            namespace Y.A.B { public class Foo { } }
            """
        );

        var global = SuffixesOf(
            """
            public void G(A.B.Foo a) { }
            public void G(Z.A.B.Foo a) { }
            """,
            "G",
            """
            namespace A.B { public class Foo { } }
            namespace Z.A.B { public class Foo { } }
            """
        );

        await Assert.That(nested).IsEqualTo("OuterInner / OtherInner");
        await Assert.That(deep).IsEqualTo("XABFoo / YABFoo");
        await Assert.That(global).IsEqualTo("GlobalABFoo / ZABFoo");
    }

    [Test]
    public async Task Climb_LeavesPrimitivesAndSharedGenericDefinitionsUnqualified()
    {
        var suffixes = SuffixesOf(
            """
            public void K(Numerics.Decimal d, int n) { }
            public void K(Other.Decimal d, int n) { }
            """,
            "K",
            """
            namespace Numerics { public class Decimal { } }
            namespace Other { public class Decimal { } }
            """
        );

        var generic = SuffixesOf(
            """
            public void M(List<Numerics.Decimal> ids) { }
            public void M(List<Other.Decimal> ids) { }
            """,
            "M",
            """
            namespace Numerics { public class Decimal { } }
            namespace Other { public class Decimal { } }
            """
        );

        await Assert.That(suffixes).IsEqualTo("NumericsDecimalInt32 / OtherDecimalInt32");
        await Assert.That(generic).IsEqualTo("ListOfNumericsDecimal / ListOfOtherDecimal");
    }

    [Test]
    public async Task Climb_RecollidesWithAnOverloadThatDidNotClimb()
    {
        var suffixes = SuffixesOf(
            """
            public void P(N1.Foo a) { }
            public void P(N2.Foo a) { }
            public void P(Z.N1Foo a) { }
            """,
            "P",
            """
            namespace N1 { public class Foo { } }
            namespace N2 { public class Foo { } }
            namespace Z { public class N1Foo { } }
            """
        );

        await Assert.That(suffixes).IsEqualTo("GlobalN1Foo / N2Foo / ZN1Foo");
    }

    [Test]
    public async Task Climb_QualifiesByAGenericContainersTypeArguments()
    {
        var suffixes = SuffixesOf(
            """
            public void M(Outer<int>.Inner a) { }
            public void M(Outer<string>.Inner a) { }
            """,
            "M",
            "public class Outer<T> { public class Inner { } }"
        );

        await Assert.That(suffixes).IsEqualTo("OuterOfInt32Inner / OuterOfStringInner");
    }

    [Test]
    public async Task ConversionGroup_IsToldApartByItsTargetType()
    {
        var compilation = IrTestHelper.Compile(
            """
            [Transpile]
            public struct Money
            {
                public long Cents;

                public static explicit operator int(Money m) => (int)m.Cents;

                public static explicit operator long(Money m) => m.Cents;
            }
            """
        );

        var diagnostics = new List<MetanoDiagnostic>();
        var type = compilation.GetTypeByMetadataName("Money")!;
        var declaration = ExtractCollecting(type, diagnostics);

        await Assert
            .That(SuffixesIn(declaration, type, "op_Explicit"))
            .IsEqualTo("MoneyToInt32 / MoneyToInt64");
        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.AmbiguousConstruct))
            .IsEmpty();
    }

    [Test]
    public async Task ExtensionGroup_LeavesASharedReceiverOut()
    {
        var shared = SuffixesOf(
            """
            public static int Twice(this Base b, int x) => x;
            public static int Twice(this Base b, string s) => 0;
            """,
            "Twice",
            "public class Base { }",
            isStatic: true
        );

        var distinct = SuffixesOf(
            """
            public static int Twice(this Base b, int x) => x;
            public static int Twice(this Other o, int x) => x;
            """,
            "Twice",
            "public class Base { } public class Other { }",
            isStatic: true
        );

        await Assert.That(shared).IsEqualTo("X / S");
        await Assert.That(distinct).IsEqualTo("BX / OX");
    }

    [Test]
    public async Task Policy_IsDeterministic()
    {
        const string members = """
            public void P(N1.Foo a) { }
            public void P(N2.Foo a) { }
            public void P(List<int> a) { }
            public void P(int[] a) { }
            """;

        const string declarations = """
            namespace N1 { public class Foo { } }
            namespace N2 { public class Foo { } }
            """;

        var first = SuffixesOf(members, "P", declarations);
        var second = SuffixesOf(members, "P", declarations);

        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task DeadEnd_ReportsMS0003_AndMarksTheOverloadsPositionally()
    {
        var consumer = CompileAliasedConsumer(
            """
            extern alias Lib1;
            extern alias Lib2;

            [Metano.Annotations.Transpile]
            public class Rates
            {
                public void K(Lib1::N.Decimal d) { }

                public void K(Lib2::N.Decimal d) { }
            }
            """
        );

        var diagnostics = new List<MetanoDiagnostic>();
        var type = consumer.GetTypeByMetadataName("Rates")!;
        var declaration = ExtractCollecting(type, diagnostics);

        var deadEnd = diagnostics.Single(d => d.Code == DiagnosticCodes.AmbiguousConstruct);
        var secondK = type.GetMembers("K")[1].Locations[0];

        await Assert
            .That(SuffixesIn(declaration, type, "K"))
            .IsEqualTo("GlobalNDecimal$1 / GlobalNDecimal$2");
        await Assert.That(deadEnd.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(deadEnd.Location).IsEqualTo(secondK);
        await Assert.That(deadEnd.Message).Contains("'GlobalNDecimal$1' and 'GlobalNDecimal$2'");
    }

    [Test]
    public async Task SingleMethod_GetsNoOverloadName()
    {
        var declaration = IrTestHelper.ExtractClass(
            """
            [Transpile]
            public class Solo
            {
                public int Add(int x) => x;
            }
            """
        );

        var add = declaration.Members!.OfType<IrMethodDeclaration>().Single();

        await Assert.That(add.OverloadName).IsNull();
        await Assert.That(add.Parameters[0].NamingSpelling).IsNull();
    }

    /// <summary>
    /// A consumer of two libraries, aliased <c>Lib1</c> and <c>Lib2</c>, that
    /// both declare <c>N.Decimal</c>.
    /// </summary>
    private static CSharpCompilation CompileAliasedConsumer(string source) =>
        CSharpCompilation.Create(
            "Consumer",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            [
                .. TranspileHelper.BaseReferences,
                CompileLibrary("Lib1").ToMetadataReference(aliases: ["Lib1"]),
                CompileLibrary("Lib2").ToMetadataReference(aliases: ["Lib2"]),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

    private static IrClassDeclaration ExtractCollecting(
        INamedTypeSymbol type,
        List<MetanoDiagnostic> diagnostics
    )
    {
        using var _ = ExtractionDiagnostics.Open(diagnostics.Add);

        return IrClassExtractor.Extract(type);
    }

    private static CSharpCompilation CompileLibrary(string assemblyName) =>
        CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText("namespace N { public class Decimal { } }")],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

    /// <summary>
    /// The suffixes of <paramref name="methodName"/>'s group in a transpiled
    /// class declaring <paramref name="members"/>, in declaration order.
    /// </summary>
    private static string SuffixesOf(
        string members,
        string methodName,
        string declarations = "",
        bool isStatic = false
    )
    {
        var modifier = isStatic ? "static " : "";

        var compilation = IrTestHelper.Compile(
            $$"""
            {{declarations}}

            [Transpile]
            public {{modifier}}class Subject
            {
                {{members}}
            }
            """
        );

        var type = compilation.GetTypeByMetadataName("Subject")!;

        return SuffixesIn(IrClassExtractor.Extract(type), type, methodName);
    }

    /// <summary>
    /// The group's suffixes in declaration order, joined by <c> / </c>. The
    /// extractor puts the group in descending arity order (a stable sort of the
    /// declaration order), so the same sort maps each overload back to its
    /// declaration.
    /// </summary>
    private static string SuffixesIn(
        IrClassDeclaration declaration,
        INamedTypeSymbol type,
        string methodName
    )
    {
        var primary = declaration
            .Members!.OfType<IrMethodDeclaration>()
            .Single(method => method.Name == methodName);

        var extracted = new[] { primary }.Concat(primary.Overloads ?? []).ToList();

        var declared = type.GetMembers(methodName).OfType<IMethodSymbol>().ToList();

        var extractionOrder = declared
            .OrderByDescending(method => method.Parameters.Length)
            .ToList();

        var suffixes = declared.Select(method =>
            SuffixOf(extracted[extractionOrder.IndexOf(method)])
        );

        return string.Join(" / ", suffixes);
    }

    private static string SuffixOf(IrMethodDeclaration method) =>
        method.OverloadName?.Suffix ?? throw new InvalidOperationException("No overload name.");
}
