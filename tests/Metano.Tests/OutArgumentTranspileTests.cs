using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>out</c> parameters and arguments (#265). A method with <c>out</c>
/// parameters returns <c>[result, out1, …]</c> instead (a <c>void</c> method
/// only the <c>out</c> values), typed as a labeled readonly tuple, or as a
/// discriminated union of tuples when <c>[NotNullWhen]</c> /
/// <c>[MaybeNullWhen]</c> ties the values to a <c>bool</c> result. Call sites
/// bind the tuple with <c>const [...]</c> ahead of the statement when the call
/// runs first, and assign it in place (<c>([, v] = call)[0]</c>) behind
/// short-circuit or conditional evaluation. BCL <c>TryGetValue</c> /
/// <c>TryParse</c> map to runtime helpers returning the same shape.
/// </summary>
public class OutArgumentTranspileTests
{
    private const string CallSitesSource = """
        using System;
        using System.Collections.Generic;

        [Transpile]
        public class Glossary
        {
            private readonly Dictionary<string, string> _words = new();
            private readonly Dictionary<string, int> _counts = new();
            private readonly IReadOnlyDictionary<string, string?> _notes =
                new Dictionary<string, string?>();
            private int _last;

            public string Translate(string key) =>
                _words.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException(key);

            public int CountOrZero(string key)
            {
                _counts.TryGetValue(key, out var count);
                return count;
            }

            public int Increment(string key)
            {
                if (!_counts.TryGetValue(key, out var count))
                    return -1;
                return count + 1;
            }

            public string Describe(string key)
            {
                var found = _words.TryGetValue(key, out var text);
                return found ? text : "?";
            }

            public bool HasNote(string key) => _notes.TryGetValue(key, out _);

            public string? Note(string key)
            {
                string? note;
                return _notes.TryGetValue(key, out note) ? note : null;
            }

            public void Remember(string key) => _counts.TryGetValue(key, out _last);

            public Func<string, int> Counter() => key => _counts.TryGetValue(key, out var c) ? c : 0;

            public string Shout(bool check, string key)
            {
                if (check && _words.TryGetValue(key, out var w) && w.Length > 3)
                    return w.ToUpper();
                return key;
            }

            public int ParseOrZero(string s) => int.TryParse(s, out var n) ? n : 0;
        }
        """;

    private const string MethodsSource = """
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;

        [Transpile]
        public class Item
        {
            public Item(string name) { Name = name; }
            public string Name { get; }
        }

        [Transpile]
        public interface ILookup
        {
            bool TryFind(string key, [NotNullWhen(true)] out Item? item);
        }

        [Transpile]
        public class Catalog : ILookup
        {
            private readonly Dictionary<string, Item> _items = new();

            public bool TryFind(string key, [NotNullWhen(true)] out Item? item)
            {
                if (_items.TryGetValue(key, out var found))
                {
                    item = found;
                    return true;
                }
                item = null;
                return false;
            }

            public bool TryName(string key, [MaybeNullWhen(false)] out string name)
            {
                var found = TryFind(key, out var item);
                name = item is null ? null : item.Name;
                return found;
            }

            public static void Split(int value, out int tens, out int units)
            {
                units = value % 10;
                if (value < 10)
                {
                    tens = 0;
                    return;
                }
                tens = (value - units) / 10;
            }

            public static bool TryHalf(int value, out int half)
            {
                half = value / 2;
                return value % 2 == 0;
            }

            public static int Remainder(int a, int b, out int quotient) =>
                a - (quotient = a / b) * b;

            public string Use(string key)
            {
                if (TryFind(key, out var item))
                    return item.Name;
                Split(42, out var tens, out _);
                return TryName(key, out var name) ? name + tens : "none";
            }
        }
        """;

    private const string OrderingSource = """
        [Transpile]
        public class Order
        {
            public static int Foo(int x, out int y)
            {
                y = x + 3;
                return x * 2;
            }

            public static bool Bar(out int z)
            {
                z = 5;
                return true;
            }

            public int Nested()
            {
                var r = Foo(Foo(1, out var a), out var c);
                return r + a + c;
            }

            public int Conditional(bool k)
            {
                var r = Foo(k && Bar(out var a) ? a : 0, out var c);
                return r + c;
            }

            public int Compound()
            {
                int x = 1;
                x += Foo(3, out x);
                return x;
            }

            public int Guard()
            {
                if (Foo(1, out var a) > 0 && Bar(out var x))
                    return x + a;
                return 0;
            }

            public int Loop()
            {
                var s = 0;
                for (var ok = Bar(out var x); ok && x < 10; x++)
                    s += x;
                return s;
            }

            public int StepLoop()
            {
                var s = 0;
                for (var ok = Bar(out var n); n < 10; n += Foo(1, out var k))
                    s += n;
                return s;
            }
        }
        """;

    [Test]
    public async Task CallSites_BindTheResultTupleInEveryPosition()
    {
        var result = TranspileHelper.Transpile(CallSitesSource);

        await Assert
            .That(result["glossary.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("out-argument-call-sites.ts"));
    }

    [Test]
    public async Task CallSites_ReportNoDiagnostics()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(CallSitesSource);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task Methods_ReturnTheirOutValuesAsATuple()
    {
        var result = TranspileHelper.Transpile(MethodsSource);

        await Assert
            .That(result["catalog.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("out-parameter-methods.ts"));
    }

    [Test]
    public async Task InterfaceMethod_DeclaresTheSameTupleAsItsImplementation()
    {
        var result = TranspileHelper.Transpile(MethodsSource);

        await Assert
            .That(result["i-lookup.ts"])
            .Contains(
                "tryFind(key: string): readonly [found: true, item: Item] "
                    + "| readonly [found: false, item: Item | null];"
            );
    }

    [Test]
    public async Task OverrideAndBaseCall_KeepTheLoweredSignature()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Diagnostics.CodeAnalysis;

            [Transpile]
            public abstract class Store
            {
                public abstract bool TryLoad(string key, [MaybeNullWhen(false)] out string value);

                public virtual void Measure(string text, out int length)
                {
                    length = text.Length;
                }
            }

            [Transpile]
            public class EmptyStore : Store
            {
                public override bool TryLoad(string key, [MaybeNullWhen(false)] out string value)
                {
                    value = null;
                    return false;
                }

                public override void Measure(string text, out int length) =>
                    base.Measure(text.Trim(), out length);
            }
            """
        );

        await Assert
            .That(result["store.ts"])
            .Contains(
                "abstract tryLoad(key: string): readonly [found: true, value: string] "
                    + "| readonly [found: false, value: string | null];"
            );
        await Assert
            .That(result["store.ts"])
            .Contains("measure(text: string): readonly [length: number] {");
        await Assert
            .That(result["empty-store.ts"])
            .Contains(
                """
                  measure(text: string): readonly [length: number] {
                    let length: number;
                    const [$out0] = super.measure(text.trim());
                    length = $out0;

                    return [length];
                  }
                """
            );
    }

    [Test]
    public async Task CallerInAnotherFile_DestructuresTheTuple()
    {
        var result = TranspileHelper.Transpile(
            MethodsSource
                + """

                [Transpile]
                public class Shelf
                {
                    public string Label(Catalog catalog, string key) =>
                        catalog.TryFind(key, out var item) ? item.Name : "?";
                }
                """
        );

        await Assert
            .That(result["shelf.ts"])
            .Contains(
                """
                    const [$found0, item] = catalog.tryFind(key);

                    return $found0 ? item.name : "?";
                """
            );
    }

    [Test]
    public async Task CallerInAnotherPackage_DestructuresTheTuple()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace MyLib
            {
                public class Digits
                {
                    public static void Split(int value, out int tens, out int units)
                    {
                        tens = value / 10;
                        units = value % 10;
                    }
                }
            }
            """;

        var consumer = """
            using MyLib;

            [assembly: TranspileAssembly]

            namespace App;

            public class Clock
            {
                public int Units(int value)
                {
                    Digits.Split(value, out _, out var units);
                    return units;
                }
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);

        await Assert
            .That(result["app/clock.ts"])
            .Contains("const [, units] = Digits.split(value);");
    }

    [Test]
    public async Task GuardedIfCondition_BindsInsideTheGuard()
    {
        var result = TranspileHelper.Transpile(CallSitesSource);

        await Assert
            .That(result["glossary.ts"])
            .Contains(
                """
                    if (check) {
                      const [$found0, w] = tryGetValue(this._words, key, null);

                      if ($found0 && w.length > 3) {
                        return w.toUpperCase();
                      }
                    }
                """
            );
    }

    [Test]
    public async Task GuardedIfCondition_WithElseOrALaterRead_StaysInPlace()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Guards
            {
                public int WithElse(bool check, Dictionary<string, int> counts)
                {
                    if (check && counts.TryGetValue("a", out var a))
                        return a;
                    else
                        return 0;
                }

                public int ReadAfter(bool check, Dictionary<string, int> counts)
                {
                    if (check && counts.TryGetValue("b", out var b))
                        b++;
                    b = 2;
                    return b;
                }
            }
            """
        );

        var output = result["guards.ts"];

        await Assert
            .That(output)
            .Contains("if (check && ([, a] = tryGetValue(counts, \"a\", 0))[0]) {");
        await Assert
            .That(output)
            .Contains("if (check && ([, b] = tryGetValue(counts, \"b\", 0))[0]) {");
    }

    [Test]
    public async Task InterfaceOutParameter_ImportsTheRuntimeTypeItNeeds()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public interface ITagSource
            {
                bool TryTags(string key, out HashSet<string> tags);
            }
            """
        );

        var output = result["i-tag-source.ts"];

        await Assert
            .That(output)
            .Contains("tryTags(key: string): readonly [found: boolean, tags: HashSet<string>];");
        await Assert.That(output).Contains("HashSet");
        await Assert.That(output).Contains("from \"metano-runtime\"");
    }

    [Test]
    public async Task ShortCircuitOperand_IsAssignedInPlace()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Words
            {
                public bool IsLong(bool check, Dictionary<string, string> words, string key) =>
                    check && words.TryGetValue(key, out var w) && w.Length > 3;

                public int Reused(bool check, Dictionary<string, int> counts)
                {
                    if (check && counts.TryGetValue("k", out var c))
                        return c;
                    c = -1;
                    return c;
                }
            }
            """
        );

        var output = result["words.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    let w: string | null;

                    return check && ([, w] = tryGetValue(words, key, null))[0] && w.length > 3;
                """
            );
        await Assert
            .That(output)
            .Contains(
                """
                    let c: number;

                    if (check && ([, c] = tryGetValue(counts, "k", 0))[0]) {
                """
            );
    }

    [Test]
    public async Task WhileCondition_BindsFreshLocalsPerIteration()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using System.Collections.Generic;

            [Transpile]
            public class Reader
            {
                public List<Func<int>> Readers(Dictionary<int, int> values)
                {
                    List<Func<int>> readers = [];
                    var i = 0;
                    while (values.TryGetValue(i, out var v))
                    {
                        readers.Add(() => v);
                        i++;
                    }
                    return readers;
                }
            }
            """
        );

        await Assert
            .That(result["reader.ts"])
            .Contains(
                """
                    while (true) {
                      const [$found0, v] = tryGetValue(values, i, 0);

                      if (!$found0) {
                        break;
                      }

                      readers.push(() => v);
                      i++;
                    }
                """
            );
    }

    [Test]
    public async Task ForCondition_MovesIntoTheBody()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Summer
            {
                public int Sum(Dictionary<int, int> values)
                {
                    var sum = 0;
                    for (var k = 0; values.TryGetValue(k, out var w); k++)
                        sum += w;
                    return sum;
                }
            }
            """
        );

        await Assert
            .That(result["summer.ts"])
            .Contains(
                """
                    for (let k = 0; ; k++) {
                      const [$found0, w] = tryGetValue(values, k, 0);

                      if (!$found0) {
                        break;
                      }

                      sum += w;
                    }
                """
            );
    }

    [Test]
    public async Task DoWhileCondition_MovesToTheBottomUnlessTheBodyContinues()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Counter
            {
                public int Plain(Dictionary<int, int> values)
                {
                    var n = 0;
                    do { n++; } while (values.TryGetValue(n, out var v) && v > 0);
                    return n;
                }

                public int Skipping(Dictionary<int, int> values)
                {
                    var n = 0;
                    do
                    {
                        n++;
                        if (n == 1) continue;
                    } while (values.TryGetValue(n, out var v) && v > 0);
                    return n;
                }
            }
            """
        );

        var output = result["counter.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    while (true) {
                      n++;
                      const [$found0, v] = tryGetValue(values, n, 0);

                      if (!($found0 && v > 0)) {
                        break;
                      }
                    }
                """
            );
        await Assert
            .That(output)
            .Contains(
                """
                    {
                      let v: number;
                      do {
                """
            );
        await Assert
            .That(output)
            .Contains("} while (([, v] = tryGetValue(values, n, 0))[0] && v > 0);");
    }

    [Test]
    public async Task SiblingLoops_DeclareTheSameOutLocalInTheirOwnScope()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Twice
            {
                public int Run(Dictionary<int, int> values, bool skip)
                {
                    var n = 0;
                    while (!skip && values.TryGetValue(n, out var v)) n += v;
                    while (!skip && values.TryGetValue(-n, out var v)) n -= v;
                    return n;
                }
            }
            """
        );

        var output = result["twice.ts"];

        await Assert.That(output.Split("let v: number;").Length - 1).IsEqualTo(2);
        await Assert
            .That(output)
            .Contains(
                """
                    while (true) {
                      let v: number;

                      if (!(!skip && ([, v] = tryGetValue(values, n, 0))[0])) {
                        break;
                      }
                """
            );
    }

    [Test]
    public async Task ExpressionBodiedGetter_BindsInsideTheGetter()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Stock
            {
                public Dictionary<string, int> Counts { get; } = new();

                public int Apples => Counts.TryGetValue("apple", out var n) ? n : 0;
            }
            """
        );

        await Assert
            .That(result["stock.ts"])
            .Contains(
                """
                  get apples(): number {
                    const [$found0, n] = tryGetValue(this.counts, "apple", 0);

                    return $found0 ? n : 0;
                  }
                """
            );
    }

    [Test]
    public async Task InitializerAndBaseArgument_BindInAnImmediatelyInvokedArrow()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Base
            {
                public Base(int seed) { Seed = seed; }
                public int Seed { get; }
            }

            [Transpile]
            public class Derived : Base
            {
                private readonly int _width = int.TryParse("12", out var w) ? w : 0;

                public Derived(string text) : base(int.TryParse(text, out var n) ? n : -1) { }
            }
            """
        );

        var output = result["derived.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                  private readonly _width: number = (() => {
                    const [$found0, w] = tryParseInt32("12");

                    return $found0 ? w : 0;
                  })();
                """
            );
        await Assert.That(output).Contains("const [$found0, n] = tryParseInt32(text);");
    }

    [Test]
    public async Task OutVarLocal_TakesTheBoundParameterType()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class Probe
            {
                public int Length(bool check, Dictionary<string, string> words) =>
                    check && words.TryGetValue("k", out var w) ? w.Length : 0;

                public int Count(bool check, Dictionary<string, int> counts) =>
                    check && counts.TryGetValue("k", out var c) ? c : 0;

                public string? Note(bool check, Dictionary<string, string?> notes) =>
                    check && notes.TryGetValue("k", out string? n) ? n : null;
            }
            """
        );

        var output = result["probe.ts"];

        await Assert.That(output).Contains("let w: string | null;");
        await Assert.That(output).Contains("let c: number;");
        await Assert.That(output).Contains("let n: string | null;");
    }

    [Test]
    public async Task OutLocal_ImportsItsType()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public record Tag(string Name);

            [Transpile]
            public class Labeler
            {
                public string Label(bool check, Dictionary<string, Tag> tags, string key) =>
                    check && tags.TryGetValue(key, out var tag) ? tag.Name : key;
            }
            """
        );

        var output = result["labeler.ts"];

        await Assert.That(output).Contains("let tag: Tag | null;");
        await Assert.That(output).Contains("Tag } from \"./tag\"");
    }

    [Test]
    public async Task MissDefault_FollowsTheValueType()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public enum Level { Low, High }

            [Transpile]
            public class Defaults
            {
                public bool Flag(Dictionary<string, bool> d) => d.TryGetValue("k", out var v) && v;
                public char Letter(Dictionary<string, char> d) => d.TryGetValue("k", out var v) ? v : 'x';
                public Level Grade(Dictionary<string, Level> d) => d.TryGetValue("k", out var v) ? v : Level.High;
                public long Big(Dictionary<string, long> d) => d.TryGetValue("k", out var v) ? v : 1L;
            }
            """
        );

        var output = result["defaults.ts"];

        await Assert.That(output).Contains("tryGetValue(d, \"k\", false)");
        await Assert.That(output).Contains("tryGetValue(d, \"k\", \"\\u0000\")");
        await Assert.That(output).Contains("tryGetValue(d, \"k\", Level.Low)");
        await Assert.That(output).Contains("tryGetValue(d, \"k\", 0)");
    }

    [Test]
    public async Task MissDefault_ForAStructWithoutLiteralReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using System.Collections.Generic;

            [Transpile]
            public class Schedule
            {
                public bool Has(Dictionary<string, DateTime> d) => d.TryGetValue("k", out var when);
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("default(System.DateTime)")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task UserStructOut_ReportsNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public struct Point { public int X; }

            [Transpile]
            public class Origin
            {
                public static bool TryGet(out Point point)
                {
                    point = new Point();
                    return true;
                }

                public int X() => TryGet(out var p) ? p.X : 0;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task TryParse_MapsToTheRuntimeHelpers()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public class Parser
            {
                public long Long(string s) => long.TryParse(s, out var n) ? n : 0;
                public double Real(string s) => double.TryParse(s, out var n) ? n : 0;
                public bool Flag(string s) => bool.TryParse(s, out var b) && b;
                public Guid Id(string s) => Guid.TryParse(s, out var g) ? g : Guid.Empty;
            }
            """
        );

        var output = result["parser.ts"];

        await Assert.That(output).Contains("= tryParseInt64(s);");
        await Assert.That(output).Contains("= tryParseFloat64(s);");
        await Assert.That(output).Contains("= tryParseBoolean(s);");
        await Assert.That(output).Contains("= UUID.tryParse(s);");
    }

    [Test]
    public async Task RefArgument_ReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Bumper
            {
                private static void Bump(ref int value) => value++;

                public int Run()
                {
                    var n = 1;
                    Bump(ref n);
                    return n;
                }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.StartsWith("'ref' argument 'n'")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task OutArgumentOfADelegate_ReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public delegate bool Parse(string text, out int value);

            [Transpile]
            public class Runner
            {
                public int Run(Parse parse) => parse("1", out var v) ? v : 0;
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.StartsWith("'out' argument 'var v'")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task OverloadsDifferingOnlyByOut_ReportMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Reader
            {
                public static int Read(string s) => s.Length;

                public static int Read(string s, out int extra)
                {
                    extra = 1;
                    return s.Length;
                }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("only by its 'out' parameters")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task NestedCall_IsBoundFirstAndAConditionalOneDeclaresItsLocal()
    {
        var output = TranspileHelper.Transpile(OrderingSource)["order.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    const [$result0, a] = Order.foo(1);
                    const [r, c] = Order.foo($result0);
                """
            );
        await Assert
            .That(output)
            .Contains(
                """
                    let a: number;
                    const [r, c] = Order.foo(k && ([, a] = Order.bar())[0] ? a : 0);
                """
            );
    }

    [Test]
    public async Task CompoundAssignment_ReadsItsTargetBeforeTheCall()
    {
        var output = TranspileHelper.Transpile(OrderingSource)["order.ts"];

        await Assert.That(output).Contains("x += ([, x] = Order.foo(3))[0];");
    }

    [Test]
    public async Task GuardConjunct_WithACallOfItsOwn_IsBoundToo()
    {
        var output = TranspileHelper.Transpile(OrderingSource)["order.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    if ($result1 > 0) {
                      const [$found0, x] = Order.bar();
                """
            );
        await Assert.That(output).Contains("const [$result1, a] = Order.foo(1);");
    }

    [Test]
    public async Task ForInitializerCall_IsBoundAheadOfTheLoop()
    {
        var output = TranspileHelper.Transpile(OrderingSource)["order.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    {
                      let [ok, x] = Order.bar();
                      for (; ; x++) {
                """
            );
    }

    [Test]
    public async Task ForInitializerCall_IsBoundAheadWhenTheConditionStaysInPlace()
    {
        var output = TranspileHelper.Transpile(OrderingSource)["order.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    {
                      let [ok, n] = Order.bar();
                      let k: number;
                      for (; n < 10; n += ([, k] = Order.foo(1))[0]) {
                """
            );
    }

    [Test]
    public async Task UnmappedBclOutMethod_ReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Concurrent;
            using System.Globalization;

            [Transpile]
            public class Unmapped
            {
                public int Styles(string s) => int.TryParse(s, NumberStyles.Any, null, out var n) ? n : 0;

                public bool Remove(ConcurrentDictionary<string, int> d) => d.TryRemove("a", out _);
            }
            """
        );

        var messages = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(messages).Contains(m => m.StartsWith("'int.TryParse' takes 'out'"));
        await Assert
            .That(messages)
            .Contains(m => m.Contains("ConcurrentDictionary<TKey, TValue>.TryRemove'"));
    }

    [Test]
    public async Task ReturnBeforeFinallyWrite_ReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Finisher
            {
                public static void Run(out int a)
                {
                    try
                    {
                        a = 2;
                        return;
                    }
                    finally
                    {
                        a = 3;
                    }
                }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("before the 'finally' block assigns them")
                )
            )
            .IsTrue();
    }
}
