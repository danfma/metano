using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The declarations of user-defined operators and conversions in TypeScript
/// (baseline FR-078, #600): one static per conversion, named by its kind,
/// direction and converted type, with no dispatcher; the operator helper
/// names (<c>__negate</c>, <c>__increment</c>, <c>__checkedAdd</c>); the
/// instance form only where the declaring type is the left operand; the type
/// parameters a static of a generic type re-declares; and the companion
/// namespace of a branded type under the same names.
/// </summary>
public class OperatorHelperDeclarationTests
{
    [Test]
    public async Task Conversions_AreOneStaticEach_NamedByKindDirectionAndType()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Money
            {
                public int Cents;

                public Money(int cents) { Cents = cents; }

                public static implicit operator Money(int c) => new Money(c);

                public static implicit operator Money?(long? c) => c is null ? null : new Money((int)c);

                public static explicit operator int(Money m) => m.Cents;

                public static explicit operator checked int(Money m) => checked(m.Cents * 1);

                public static explicit operator long(Money m) => m.Cents * 1000L;

                public static implicit operator string(Money m) => "$" + m.Cents;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await TranspileHelper.AssertGolden("operator-helper-conversions.ts", files["money.ts"]);
    }

    [Test]
    public async Task Operators_TakeTheirKindNames_AndAnInstanceFormOnlyForTheirLeftOperand()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Meters
            {
                public int V;

                public Meters(int v) { V = v; }

                public static Meters operator -(Meters a) => new(-a.V);

                public static Meters operator +(Meters a) => a;

                public static Meters operator -(Meters a, Meters b) => new(a.V - b.V);

                public static Meters operator +(int a, Meters m) => new(a + m.V);

                public static Meters operator ++(Meters a) => new(a.V + 1);

                public static Meters operator --(Meters a) => new(a.V - 1);

                public static Meters operator checked -(Meters a, Meters b) => new(checked(a.V - b.V));

                public static Meters operator >>>(Meters a, int n) => new(a.V >>> n);

                public static Meters operator *(Meters a, int k) => new(a.V * k);

                public static Meters operator *(int k, Meters a) => new(a.V * k * 10);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await TranspileHelper.AssertGolden("operator-helper-names.ts", files["meters.ts"]);
    }

    [Test]
    public async Task StaticsOfAGenericType_RedeclareItsTypeParameters()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Box<T>
            {
                public T Item;

                public int Count;

                public Box(T item, int count) { Item = item; Count = count; }

                public static Box<T> operator +(Box<T> b, int n) => new(b.Item, b.Count + n);

                public static Box<T> operator +(Box<T> b, long n) => new(b.Item, b.Count + (int)n);

                public static Box<T> operator -(Box<T> b, int n) => new(b.Item, b.Count - n);

                public static implicit operator T(Box<T> b) => b.Item;
            }
            """
        );

        await TranspileHelper.AssertGolden("operator-helper-generic.ts", result["box.ts"]);
    }

    [Test]
    public async Task BrandedCompanion_UsesTheClassNames_AndSkipsOnlyAnIdentityUnwrapBody()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct Qty(int Value)
            {
                public static Qty operator +(Qty a, Qty b) => new(a.Value + b.Value);

                public static implicit operator Qty(int v) => new(v * 2);

                public static explicit operator int(Qty q) => q.Value + 7;
            }

            [Branded]
            public readonly record struct Slug(string Value)
            {
                public static implicit operator string(Slug s) => s.Value;
            }
            """
        );

        await TranspileHelper.AssertGolden("operator-helper-branded.ts", result["qty.ts"]);
        await Assert.That(result["slug.ts"]).DoesNotContain("__implicitToString");
    }

    [Test]
    public async Task ConversionsWithOneSpelling_ReportADeadEnd()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Money
            {
                [Name("toInt32")]
                public static explicit operator long(Money m) => 0;

                public static explicit operator int(Money m) => 0;
            }
            """
        );

        var deadEnd = diagnostics.Single(d => d.Code == DiagnosticCodes.AmbiguousConstruct);

        await Assert.That(deadEnd.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(deadEnd.Message).Contains("spell the same name 'ToInt32'");
    }

    [Test]
    public async Task AConversionNameTakenByAMember_IsAnError_NotARename()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Money
            {
                public static int __toInt32() => 0;

                public static explicit operator int(Money m) => 1;
            }
            """
        );

        var collision = diagnostics.Single(d => d.Code == DiagnosticCodes.AmbiguousConstruct);

        await Assert.That(collision.Message).Contains("is emitted as '__toInt32'");
        await Assert.That(files["money.ts"]).DoesNotContain("__toInt32$");
    }

    [Test]
    public async Task AnInheritedConversionStatic_DoesNotClaimTheDerivedOne()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Base
            {
                public static implicit operator Base(int v) => new Base();
            }

            [Transpile]
            public class Der : Base
            {
                public static implicit operator Der(int v) => new Der();
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(files["der.ts"]).Contains("static __implicitFromInt32(v: number): Der {");
    }
}
