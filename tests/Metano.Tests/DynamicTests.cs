using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// C# <c>dynamic</c> (spec 011 FR-030, stage 011e): <c>any</c> in signatures,
/// the operations of a <c>dynamic</c> receiver passed straight through with
/// verbatim member names (the <c>System.Object</c> members lowered as on an
/// <c>object</c>), late-bound calls of a typed receiver bound to their single
/// candidate or to the runtime dispatcher, implicit conversions that emit
/// nothing, numeric casts through the <c>metano-runtime</c> helpers, and
/// <c>MS0053</c> for what cannot pass through.
/// </summary>
public class DynamicTests
{
    [Test]
    public async Task MemberAccess_PassesThroughWithVerbatimNames()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using System.Collections.Generic;

            [Transpile]
            public class Probe
            {
                public dynamic Value = null!;

                public List<dynamic> Items = new();

                public void Ov(int x) { }

                public void Ov(string s) { }

                public static int Twice(int x) => x * 2;

                public static int Pick(int x) => x;

                public static int Pick(string s) => 1;

                public Probe() { }

                public Probe(int x) { }

                public Probe(string s) { }

                public dynamic Members(dynamic d)
                {
                    var x = d.foo.Bar(1)[2] + d.n;
                    d.AppName = "app";
                    d[0] = 2;
                    d.Generic<int>();
                    d(3);
                    d.Count += 1;
                    d[1]++;
                    var maybe = d?.inner;
                    Func<dynamic, int> length = z => z.Length;
                    dynamic local = 1;
                    local.Grow();

                    return x;
                }

                public void ObjectMembers(dynamic d)
                {
                    var s = d.ToString();
                    var h = d.GetHashCode();
                    var e = d.Equals(1);
                }

                public void Operators(dynamic d, int i, long big, string text)
                {
                    var a = d + 1L;
                    var b = d * i;
                    var c = text + d;
                    var n = d == null;
                    var neg = -d;
                    var not = !d;
                    var masked = d & 1;
                    i += d;
                    d += 1L;
                    d++;
                }

                public void LateBound(dynamic d, Probe other)
                {
                    other.Ov(d);
                    var twice = Twice(d);
                    var picked = Pick(d);
                    var created = new Probe(d);
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("dynamic-member-access.ts", result["probe.ts"]);
    }

    [Test]
    public async Task ImplicitConversions_EmitNothing()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            [Transpile]
            public class Shape { }

            [Transpile]
            public class Conversions
            {
                public static void Take(string s) { }

                public string Implicit(dynamic d)
                {
                    string s = d;
                    int n = d;
                    long l = d;
                    decimal m = d;
                    int? maybe = d;
                    Shape shape = d;
                    object o = d;
                    Take(d);
                    var hole = $"{d}";

                    if (d)
                    {
                    }

                    foreach (string x in d)
                    {
                    }

                    var list = new List<int> { d };

                    return d;
                }

                public async Task<int> Awaited(dynamic d) => await d;
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "dynamic-implicit-conversions.ts",
            result["conversions.ts"]
        );
    }

    [Test]
    public async Task ExplicitCasts_ConvertNumbersAndCastReferencesAsFromObject()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Shape { }

            [Transpile]
            public interface IHasArea
            {
                double Area();
            }

            [Transpile]
            public enum Color { Red, Green }

            [Transpile]
            public struct Point
            {
                public int X;
            }

            [Transpile]
            public class Casts
            {
                public void All(dynamic d)
                {
                    var i = (int)d;
                    var b = (byte)d;
                    var checkedByte = checked((byte)d);
                    var f = (float)d;
                    var x = (double)d;
                    var l = (long)d;
                    var u = (ulong)d;
                    var big = (System.Numerics.BigInteger)d;
                    var c = (char)d;
                    var m = (decimal)d;
                    var maybe = (int?)d;
                    var shape = (Shape)d;
                    var area = (IHasArea)d;
                    var text = (string)d;
                    var o = (object)d;
                    var color = (Color)d;
                    var point = (Point)d;
                    var flag = (bool)d;
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("dynamic-explicit-casts.ts", result["casts.ts"]);
    }

    [Test]
    public async Task Patterns_TestADynamicValueAsAnObject()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Circle { }

            [Transpile]
            public class Patterns
            {
                public int Classify(dynamic d)
                {
                    if (d is Circle c)
                        return 1;

                    if (d is null)
                        return 0;

                    var circle = d as Circle;

                    return d switch
                    {
                        int n => n,
                        string s => s.Length,
                        _ => -1,
                    };
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("dynamic-patterns.ts", result["patterns.ts"]);
    }

    [Test]
    public async Task InitializerOnLateBoundConstruction_ReportsMs0053()
    {
        var error = await SingleDynamicError(
            """
            [Transpile]
            public class Box
            {
                public int P;

                public Box(int x) { }

                public Box(string s) { }

                public static Box Make(dynamic d) => new Box(d) { P = 1 };
            }
            """
        );

        await Assert.That(LocatedSource.TextOf(error)).IsEqualTo("new Box(d) { P = 1 }");
    }

    [Test]
    public async Task InitializerElementWithSeveralAddCandidates_ReportsMs0053()
    {
        var error = await SingleDynamicError(
            """
            using System.Collections;

            [Transpile]
            public class Bag : IEnumerable
            {
                public void Add(int x) { }

                public void Add(string s) { }

                public IEnumerator GetEnumerator() => null!;

                public static Bag Make(dynamic d) => new Bag { d };
            }
            """
        );

        await Assert.That(LocatedSource.TextOf(error)).IsEqualTo("d");
    }

    [Test]
    [Arguments("d.Run(name: 1)")]
    [Arguments("d.Run(ref v)")]
    [Arguments("d[i: 1]")]
    public async Task NamedOrByReferenceArgumentOnDynamicReceiver_ReportsMs0053(string site)
    {
        var error = await SingleDynamicError(
            $$"""
            [Transpile]
            public class Calls
            {
                public object Run(dynamic d, int v) => {{site}};
            }
            """
        );

        await Assert.That(LocatedSource.TextOf(error)).IsEqualTo(site);
    }

    [Test]
    [Arguments("(Money)d")]
    [Arguments("money + d")]
    public async Task UserDefinedConversionOrOperator_ReportsMs0053(string site)
    {
        var error = await SingleDynamicError(
            $$"""
            [Transpile]
            public struct Money
            {
                public decimal V;

                public static implicit operator Money(decimal v) => new Money { V = v };

                public static Money operator +(Money a, Money b) => a;
            }

            [Transpile]
            public class Uses
            {
                public object Run(dynamic d, Money money) => {{site}};
            }
            """
        );

        await Assert.That(LocatedSource.TextOf(error)).IsEqualTo(site);
    }

    [Test]
    public async Task ImplicitConversionToUserConvertibleType_ReportsMs0053()
    {
        var error = await SingleDynamicError(
            """
            [Transpile]
            public struct Money
            {
                public decimal V;

                public static implicit operator Money(decimal v) => new Money { V = v };
            }

            [Transpile]
            public class Uses
            {
                public Money Run(dynamic d)
                {
                    Money m = d;

                    return m;
                }
            }
            """
        );

        await Assert.That(LocatedSource.TextOf(error)).IsEqualTo("d");
    }

    [Test]
    [Arguments("System.Console.WriteLine(d)")]
    [Arguments("text.Contains(d)")]
    [Arguments("new System.Text.StringBuilder(d)")]
    public async Task SeveralCandidatesWithoutADispatcher_ReportsMs0053(string site)
    {
        var error = await SingleDynamicError(
            $$"""
            [Transpile]
            public class Calls
            {
                public void Run(dynamic d, string text)
                {
                    {{site}};
                }
            }
            """
        );

        await Assert.That(LocatedSource.TextOf(error)).IsEqualTo(site);
        await Assert.That(error.Message).Contains("to object");
    }

    [Test]
    public async Task CandidatesNotEqualToTheDispatcherBranches_ReportsMs0053()
    {
        // The dispatcher of `Show` also branches over `Show(Shape)`, which the
        // runtime binder never picks for this call (it takes a `Circle`):
        // the dispatcher would choose among more overloads than C# does.
        var error = await SingleDynamicError(
            """
            [Transpile]
            public class Shape { }

            [Transpile]
            public class Circle { }

            [Transpile]
            public class Shows
            {
                public static void Show(Circle c, int n) { }

                public static void Show(Circle c, string s) { }

                public static void Show(Shape s, int n) { }

                public static void Run(dynamic d, Circle c) => Show(c, d);
            }
            """
        );

        await Assert.That(LocatedSource.TextOf(error)).IsEqualTo("Show(c, d)");
    }

    [Test]
    public async Task TestCompilation_ReferencesMicrosoftCSharp()
    {
        var names = TranspileHelper.BaseReferences.Select(reference =>
            Path.GetFileName(reference.Display)
        );

        await Assert.That(names).Contains("Microsoft.CSharp.dll");
    }

    [Test]
    public async Task DartTarget_KeepsTheSourceForms()
    {
        // D28: Dart keeps the symbol-less extraction it had before FR-030, so
        // its output is the same as before the dynamic nodes existed.
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Probe
            {
                public dynamic Value = null!;

                public static void Show(int x) { }

                public static void Show(string s) { }

                public object Read(dynamic d)
                {
                    var n = (int)d;
                    Show(d);
                    System.Console.WriteLine(d);

                    return d.Name + d[0];
                }
            }
            """
        );

        var dart = files["probe.dart"];

        await Assert.That(dart).Contains("dynamic value");
        await Assert.That(dart).Contains("d.name + d[0]");
        await Assert.That(dart).Contains("d as int");
        await Assert.That(dart).DoesNotContain("UnsupportedError");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedDynamicOperation))
            .IsEmpty();
    }

    [Test]
    public async Task CompoundAssignments_KeepTheTargetShapeAndStoreConversion()
    {
        var result = TranspileHelper.Transpile(
            """
            public static class BagExtensions
            {
                extension(Bag bag)
                {
                    public int Twice
                    {
                        get => bag.Value * 2;
                        set => bag.Value = value / 2;
                    }
                }
            }

            [Transpile]
            public class Bag
            {
                public int Value;
            }

            [Transpile]
            public class Stores
            {
                public void Run(dynamic d, Bag bag, int i, char c, short s, uint u, double x)
                {
                    bag.Twice += d;
                    i += d;
                    c += d;
                    s += d;
                    u -= d;
                    d += 1L;
                    var same = x == d;
                    var less = x < d;
                }
            }
            """
        );

        await TranspileHelper.AssertGolden("dynamic-compound-assignments.ts", result["stores.ts"]);
    }

    private static async Task<MetanoDiagnostic> SingleDynamicError(string source)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedDynamicOperation);

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);

        return error;
    }
}
