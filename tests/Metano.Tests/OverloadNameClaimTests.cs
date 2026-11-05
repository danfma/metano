using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The TypeScript claim pass of spec 013 (FR-004, FR-020, FR-034,
/// contracts/diagnostics.md): every <c>$</c> rename reports <c>MS0067</c> at
/// the renamed overload, and a derived user static named like an inherited
/// factory reports <c>MS0068</c> at the user member.
/// </summary>
public class OverloadNameClaimTests
{
    [Test]
    public async Task FactoryClaimedByAUserMember_ReportsMs0067AtTheConstructor()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Fee
            {
                public static int FromCents = 3;

                public Fee(int cents) { }

                public Fee(string label) { }
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.OverloadNameShadowed);

        await Assert.That(report).IsNotNull();

        await Assert.That(report!.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert
            .That(report.Message)
            .IsEqualTo(
                "Factory 'Fee.fromCents' collides with the user member 'fromCents'; the generated "
                    + "factory and selector are emitted as 'fromCents$' and 'byFeeCents$' "
                    + "(spec 013 FR-004)."
            );
        await Assert.That(LineOf(report)).IsEqualTo(8);
        await Assert.That(files["fee.ts"]).Contains("static fromCents$(cents: number): Fee {");
    }

    [Test]
    public async Task FastPathClaimedByAFactory_ReportsMs0067AtTheMethod()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Cash
            {
                public Cash(decimal amount) { }

                public Cash(int cents) { }

                public static Cash From(decimal amount) => new Cash(amount);

                public static Cash From(int cents) => new Cash(cents);
            }
            """
        );

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.OverloadNameShadowed)
            .OrderBy(LineOf)
            .ToList();

        await Assert.That(reports.Count).IsEqualTo(2);
        await Assert
            .That(reports[0].Message)
            .IsEqualTo(
                "Fast path 'Cash.fromAmount' of 'From(decimal amount)' collides with the factory "
                    + "of 'Cash(decimal amount)'; it is emitted as 'fromAmount$' (spec 013 FR-004)."
            );
        await Assert.That(LineOf(reports[0])).IsEqualTo(10);
        await Assert.That(LineOf(reports[1])).IsEqualTo(12);
        await Assert.That(files["cash.ts"]).Contains("return Cash.fromCents$(args[0] as number);");
    }

    [Test]
    public async Task SelectorClaimedByAUserMember_RenamesTheAbstractClassSelector()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Shape
            {
                public static int ByShapeScale = 1;

                protected Shape(int scale) { }

                protected Shape(string name) { }
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.OverloadNameShadowed);

        await Assert.That(report).IsNotNull();

        await Assert
            .That(report!.Message)
            .IsEqualTo(
                "Selector 'Shape.byShapeScale' collides with the user member 'byShapeScale'; the "
                    + "generated selector is emitted as 'byShapeScale$' (spec 013 FR-004)."
            );
        await Assert.That(files["shape.ts"]).Contains("typeof Shape.byShapeScale$, scale: number");
    }

    [Test]
    public async Task InstanceFastPathClaimedByAUserMethod_ReportsMs0067()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Adder
            {
                public int AddX() => 0;

                public int Add(int x) => x;

                public int Add(string s) => s.Length;
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.OverloadNameShadowed);

        await Assert.That(report).IsNotNull();

        await Assert
            .That(report!.Message)
            .IsEqualTo(
                "Fast path 'Adder.addX' of 'Add(int x)' collides with the user member 'addX'; it "
                    + "is emitted as 'addX$' (spec 013 FR-004)."
            );
        await Assert.That(LineOf(report)).IsEqualTo(8);
        await Assert.That(files["adder.ts"]).Contains("return this.addX$(args[0] as number);");
    }

    [Test]
    public async Task DerivedUserStaticNamedLikeAnInheritedFactory_ReportsMs0068()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Money
            {
                public Money(decimal amount) { }

                public Money(int cents) { }
            }

            [Transpile]
            public class Coupon : Money
            {
                public Coupon(int cents) : base(cents) { }

                public static Coupon FromAmount(string text) => new Coupon(text.Length);
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.OverloadFastPathHijacked);

        await Assert.That(report).IsNotNull();

        await Assert.That(report!.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert
            .That(report.Message)
            .IsEqualTo(
                "'Coupon.FromAmount' would redeclare the generated factory 'fromAmount' of "
                    + "'Money(decimal amount)'; rename the member or give it [Name], or pin the "
                    + "constructor's suffix with [Name] (spec 013 FR-034)."
            );
        await Assert.That(LineOf(report)).IsEqualTo(16);
    }

    [Test]
    public async Task SingleConstructor_GetsNoSelectorNoFactoryAndNoClaim()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Solo
            {
                public static int FromV = 1;

                public Solo(int v) { }
            }
            """
        );

        await Assert.That(files["solo.ts"]).Contains("static fromV: number = 1;");
        await Assert.That(files["solo.ts"]).Contains("constructor(v: number)");
        await Assert.That(files["solo.ts"]).DoesNotContain("unique symbol");
        await Assert.That(files["solo.ts"]).DoesNotContain("static fromV(");
        await Assert
            .That(FindDiagnostic(diagnostics, DiagnosticCodes.OverloadNameShadowed))
            .IsNull();
    }

    [Test]
    public async Task TypeSpelledGroup_ReportsMs0067AtTheRenamedOverload()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Names
            {
                public string FString() => "user";

                public string F(int a) => "int";

                public string F(string a) => "string";
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.OverloadNameShadowed);

        await Assert.That(report).IsNotNull();

        await Assert.That(report!.Message).Contains("'F(string a)'");
        await Assert.That(LineOf(report)).IsEqualTo(10);
    }

    [Test]
    public async Task StaticFastPathMeetingAnInheritedFactory_NamesTheBaseType()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Money
            {
                public Money(decimal amount) { }

                public Money(int cents) { }
            }

            [Transpile]
            public class Cash : Money
            {
                public Cash(int cents) : base(cents) { }

                public static Cash From(decimal amount) => new Cash(1);

                public static Cash From(int cents) => new Cash(cents);
            }
            """
        );

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.OverloadNameShadowed)
            .OrderBy(LineOf)
            .ToList();

        await Assert.That(reports.Count).IsEqualTo(2);
        await Assert
            .That(reports[0].Message)
            .IsEqualTo(
                "Fast path 'Cash.fromAmount' of 'From(decimal amount)' collides with the factory "
                    + "of 'Money(decimal amount)' inherited from 'Money'; it is emitted as "
                    + "'fromAmount$' (spec 013 FR-004)."
            );
        await Assert
            .That(reports[1].Message)
            .IsEqualTo(
                "Fast path 'Cash.fromCents' of 'From(int cents)' collides with the factory of "
                    + "'Money(int cents)' inherited from 'Money'; it is emitted as 'fromCents$' "
                    + "(spec 013 FR-004)."
            );
        await Assert.That(files["cash.ts"]).Contains("static fromAmount$(amount: Decimal)");
    }

    [Test]
    public async Task QualifiedFactoryClaimedByAUserMember_StaysQualified()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Box<T>
            {
                public Box(T value) { }

                public Box(int n) { }
            }

            [Transpile]
            public class IntBox : Box<int>
            {
                public static int FromIntBoxValue = 1;

                public IntBox(int value) : base(value) { }

                public IntBox(int value, int extra) : base(value + extra) { }
            }
            """
        );

        await Assert
            .That(files["int-box.ts"])
            .Contains("static fromIntBoxValue$(value: number): IntBox {");
        await Assert
            .That(files["int-box.ts"])
            .Contains("protected static readonly byIntBoxValue$: unique symbol");
        await Assert
            .That(FindDiagnostic(diagnostics, DiagnosticCodes.OverloadNameShadowed))
            .IsNotNull();
    }

    [Test]
    public async Task InheritedFactory_SwappedBaseTypeParameters_RebindsBothAtOnce()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class A<K, V>
            {
                public A(K k, V v) { }

                public A(int n) { }
            }

            [Transpile]
            public class B<K, V> : A<V, K>
            {
                public B(V k, K v) : base(k, v) { }

                public B(int n) : base(n) { }
            }
            """
        );

        await Assert
            .That(files["b.ts"])
            .Contains("static override fromKV<K, V>(k: V, v: K): B<K, V> {");
        await Assert.That(files["b.ts"]).DoesNotContain("(k: K, v: V): B<K, V>");
    }

    /// <summary>
    /// <c>MS0067</c> reason 3 (claim step 7): a <c>new</c> method's candidate
    /// meets the base's fast path, which it must not override.
    /// </summary>
    [Test]
    public async Task HidingMethodMeetingAnInheritedFastPath_ReportsMs0067Reason3()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Base
            {
                public virtual int Add(int x) => x;

                public virtual int Add(string s) => s.Length;
            }

            [Transpile]
            public class Hider : Base
            {
                public new int Add(int x) => 100;
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.OverloadNameShadowed);

        await Assert.That(report).IsNotNull();

        await Assert.That(report!.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert
            .That(report.Message)
            .IsEqualTo(
                "Fast path 'Hider.addX' of 'Add(int x)' collides with the fast path of "
                    + "'Add(int x)' inherited from 'Base'; it is emitted as 'addX$' "
                    + "(spec 013 FR-004)."
            );
        await Assert.That(LineOf(report)).IsEqualTo(14);
        await Assert.That(files["hider.ts"]).Contains("  addX$(x: number): number {");
    }

    /// <summary>
    /// <c>MS0067</c> reason 3 against a <c>private</c> base fast path: a
    /// derived type's new overload would redeclare it (TS2416).
    /// </summary>
    [Test]
    public async Task NewOverloadMeetingAPrivateBaseFastPath_ReportsMs0067Reason3()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Ledger
            {
                public int Add(int x) => x;

                private int Add(string s) => s.Length;
            }

            [Transpile]
            public class Shorty : Ledger
            {
                public int Add(short s) => 7;
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.OverloadNameShadowed);

        await Assert.That(report).IsNotNull();
        await Assert
            .That(report!.Message)
            .IsEqualTo(
                "Fast path 'Shorty.addS' of 'Add(short s)' collides with the fast path of "
                    + "'Add(string s)' inherited from 'Ledger'; it is emitted as 'addS$' "
                    + "(spec 013 FR-004)."
            );
        await Assert.That(LineOf(report)).IsEqualTo(14);
        await Assert.That(files["shorty.ts"]).Contains("return this.addS$(args[0] as number);");
    }

    /// <summary>
    /// <c>MS0068</c> reason 1 (FR-034): a user member named like an inherited
    /// fast path would override it, and the inherited dispatcher would call
    /// the user member.
    /// </summary>
    [Test]
    public async Task UserMemberNamedLikeAnInheritedFastPath_ReportsMs0068()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Base
            {
                public int Add(int x) => x;

                public int Add(string s) => s.Length;
            }

            [Transpile]
            public class Derived : Base
            {
                public int AddX() => 0;
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.OverloadFastPathHijacked);

        await Assert.That(report).IsNotNull();

        await Assert.That(report!.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert
            .That(report.Message)
            .IsEqualTo(
                "'Derived.AddX' would override the generated fast path 'addX' of "
                    + "'Base.Add(int x)'; rename the member, or rename the root overload's "
                    + "parameter or pin it with [Name] (spec 013 FR-034)."
            );
        await Assert.That(LineOf(report)).IsEqualTo(14);
    }

    /// <summary>
    /// <c>MS0068</c> reason 1 for an implementation: an interface root whose
    /// name a base declares as the fast path of another overload
    /// (<c>rX(x: string)</c> against <c>R(int x)</c>) cannot be renamed, and
    /// overriding it would change the base overload's signature.
    /// </summary>
    [Test]
    public async Task RootNameHeldByAnUnrelatedBaseFastPath_ReportsMs0068()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IA
            {
                int R(int x);

                int R(string s);
            }

            [Transpile]
            public class A4
            {
                public int R(string x) => 1;

                public int R(bool b) => 2;
            }

            [Transpile]
            public class D4 : A4, IA
            {
                public int R(int x) => 3;

                public new int R(string s) => 4;
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.OverloadFastPathHijacked);

        await Assert.That(report).IsNotNull();
        await Assert
            .That(report!.Message)
            .IsEqualTo(
                "'D4.R' would override the generated fast path 'rX' of 'A4.R(string x)'; "
                    + "rename the member, or rename the root overload's parameter or pin it "
                    + "with [Name] (spec 013 FR-034)."
            );
        await Assert.That(files["d4.ts"]).Contains("  rX(x: number): number {");
        await Assert.That(files["d4.ts"]).DoesNotContain("override rX(");
    }

    /// <summary>
    /// A re-implementation is recognized through the base's interface
    /// mapping, not the base's signature text: a generic base implementing
    /// the root (<c>R(T x)</c> as <c>A&lt;int&gt;</c>) and a hidden generic
    /// method under another type-parameter name are overridden, with no
    /// <c>MS0068</c>.
    /// </summary>
    [Test]
    public async Task ReimplementationOfAGenericBase_OverridesWithoutMs0068()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IG<T>
            {
                int R(T x);

                int R(string s);
            }

            [Transpile]
            public class A<T> : IG<T>
            {
                public int R(T x) => 1;

                public int R(string s) => 2;
            }

            [Transpile]
            public class D : A<int>, IG<int>
            {
                public new int R(int x) => 3;
            }

            [Transpile]
            public interface IM
            {
                int M<T>(T x);

                int M(string s);
            }

            [Transpile]
            public class B : IM
            {
                public int M<T>(T x) => 1;

                public int M(string s) => 2;
            }

            [Transpile]
            public class E : B, IM
            {
                public new int M<U>(U x) => 3;
            }
            """
        );

        await Assert
            .That(FindDiagnostic(diagnostics, DiagnosticCodes.OverloadFastPathHijacked))
            .IsNull();
        await Assert.That(files["d.ts"]).Contains("  override rX(x: number): number {");
        await Assert.That(files["e.ts"]).Contains("  override mX<U>(x: U): number {");
    }

    /// <summary>
    /// A base fast path for a type of the same simple name in another
    /// namespace is another overload, whatever the signature texts print:
    /// <c>MS0068</c>.
    /// </summary>
    [Test]
    public async Task SameSimpleNameInAnotherNamespace_ReportsMs0068()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace N1
            {
                [Transpile]
                public class Foo { }
            }

            namespace N2
            {
                [Transpile]
                public class Foo { }

                [Transpile]
                public interface IA
                {
                    int R(Foo x);

                    int R(string s);
                }

                [Transpile]
                public class A
                {
                    public int R(N1.Foo x) => 1;

                    public int R(bool b) => 2;
                }

                [Transpile]
                public class D : A, IA
                {
                    public int R(Foo x) => 3;

                    public int R(string s) => 4;
                }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Count(diagnostic =>
                    diagnostic.Code == DiagnosticCodes.OverloadFastPathHijacked
                )
            )
            .IsGreaterThanOrEqualTo(1);
        await Assert.That(files["n2/d.ts"]).DoesNotContain("override rX(");
    }

    /// <summary>
    /// <c>MS0069</c> (FR-036): a type implementing one generic interface
    /// twice keeps runtime dispatch, reported at the type that adds the
    /// second construction — not at a type that inherits both.
    /// </summary>
    [Test]
    public async Task MultiplyConstructedInterface_ReportsMs0069AtTheTypeThatAddsAConstruction()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            OverloadVirtualGoldenTests.ConvSource
        );

        var reports = diagnostics
            .Where(diagnostic =>
                diagnostic.Code == DiagnosticCodes.MultiplyConstructedOverloadGroup
            )
            .OrderBy(LineOf)
            .ToList();

        await Assert.That(reports.Select(LineOf)).IsEquivalentTo([12, 38]);
        await Assert
            .That(
                diagnostics.Any(diagnostic => diagnostic.Severity == MetanoDiagnosticSeverity.Error)
            )
            .IsFalse();
        await Assert.That(reports[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert
            .That(reports[0].Message)
            .IsEqualTo(
                "'C' implements 'IConv<T>' more than once; calls to 'Conv' keep runtime "
                    + "dispatch (spec 013 FR-036)."
            );
    }

    /// <summary>
    /// <c>MS0069</c>'s note: two constructions that lower to one runtime shape
    /// (<c>List&lt;int&gt;</c> and <c>int[]</c>, both arrays) cannot be told
    /// apart, so the first branch wins.
    /// </summary>
    [Test]
    public async Task IndistinguishableConstructions_AddTheFirstBranchWinsNote()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface ISink<T>
            {
                int Put(T x);

                int Put(T x, int n);
            }

            [Transpile]
            public class Sink : ISink<System.Collections.Generic.List<int>>, ISink<int[]>
            {
                public int Put(System.Collections.Generic.List<int> x) => 1;

                public int Put(System.Collections.Generic.List<int> x, int n) => 2;

                public int Put(int[] x) => 3;

                public int Put(int[] x, int n) => 4;
            }
            """
        );

        var report = FindDiagnostic(diagnostics, DiagnosticCodes.MultiplyConstructedOverloadGroup);

        await Assert
            .That(report!.Message)
            .IsEqualTo(
                "'Sink' implements 'ISink<T>' more than once; calls to 'Put' keep runtime "
                    + "dispatch (spec 013 FR-036). The constructions 'ISink<List<int>>' and "
                    + "'ISink<int[]>' cannot be told apart at run time; the first branch wins."
            );
    }

    /// <summary>
    /// DECIDED-31: an explicit overload with the signature of a class member
    /// of the same name keeps today's <c>MS0031</c> and is dropped; one with
    /// another signature joins the class's dispatcher.
    /// </summary>
    [Test]
    public async Task ExplicitOverloadWithAClassMembersSignature_KeepsMs0031()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IShape
            {
                int Area(int s);

                int Area(int w, int h);
            }

            [Transpile]
            public class Clash : IShape
            {
                int IShape.Area(int s) => s * s;

                int IShape.Area(int w, int h) => w * h;

                public int Area(int side) => side;
            }
            """
        );

        await Assert
            .That(diagnostics.Count(diagnostic => diagnostic.Code == "MS0031"))
            .IsEqualTo(1);
        await Assert.That(files["clash.ts"]).Contains("areaWH(w: number, h: number): number {");
    }

    /// <summary>
    /// The only diagnostic with <paramref name="code"/>, or <c>null</c> when
    /// none has it; throws when several have it.
    /// </summary>
    private static MetanoDiagnostic? FindDiagnostic(
        IEnumerable<MetanoDiagnostic> diagnostics,
        string code
    ) => diagnostics.SingleOrDefault(diagnostic => diagnostic.Code == code);

    /// <summary>
    /// The diagnostic's zero-based line in the compiled source, which
    /// <see cref="TranspileHelper"/> opens with three lines of usings.
    /// </summary>
    private static int LineOf(MetanoDiagnostic diagnostic) =>
        diagnostic.Location!.GetLineSpan().StartLinePosition.Line;
}
