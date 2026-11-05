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
        await Assert.That(files["cash.ts"]).Contains("private static fromAmount$(amount: Decimal)");
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
