namespace Metano.Tests;

/// <summary>
/// Regression tests for Wave 6 Group D (Dart printer, calls, argument shapes):
/// #359, #402, #408, #577, #585, #586, #587, #589, #387.
/// </summary>
public class DartGroupDFixesTests
{
    [Test]
    public async Task DollarSignInStringAndInterpolation_IsEscaped_359()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public static class Texts
            {
                public static string Plain() => "$&";

                public static string Interp(string x) => $"{x}$&";
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var dart = files["texts.dart"];
        await Assert.That(dart).Contains("'\\$&'");
        await Assert.That(dart).Contains("'${x ?? ''}\\$&'");
    }

    [Test]
    public async Task NestedObjectPattern_EmitsTypeName_359()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public record Person(string Name);

            [Transpile]
            public static class Matcher
            {
                public static bool IsEmptyName(object o) => o switch
                {
                    Person { Name: { Length: 0 } } => true,
                    _ => false,
                };

                public static bool IsEmptyExtended(Person p) => p switch
                {
                    { Name.Length: 0 } => true,
                    _ => false,
                };
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var dart = files["matcher.dart"];
        await Assert.That(dart).Contains("Person(name: String(length: 0)) => true");
        await Assert.That(dart).Contains("Person(name: String(length: 0)) => true");
    }

    [Test]
    public async Task LocalDeclarationsAndReads_UseSameCamelCaseName_402()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using System;
            using System.Collections.Generic;

            [Transpile]
            public static class Locals
            {
                public static int Sum(List<int> Items)
                {
                    var Total = 0;
                    foreach (var Item in Items)
                    {
                        Total += Item;
                    }
                    try
                    {
                        return Total;
                    }
                    catch (Exception Ex)
                    {
                        return Ex.Message.Length;
                    }
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var dart = files["locals.dart"];
        await Assert.That(dart).Contains("var total = 0;");
        await Assert.That(dart).Contains("for (var item in items)");
        await Assert.That(dart).Contains("total += item;");
        await Assert.That(dart).Contains("on Exception catch (ex)");
        await Assert.That(dart).Contains("return ex.message.length;");
    }

    [Test]
    public async Task AwaitInsideLetAndSequenceClosures_MarksClosureAsyncAndAwaits_408()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using System.Threading.Tasks;

            [Transpile]
            public static class AsyncOps
            {
                public static Task<int> GetValue() => Task.FromResult(42);

                public static async Task<int> SequenceWithAwait()
                {
                    var x = 1;
                    return (x = await GetValue(), x + 1).Item2;
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var dart = files["async_ops.dart"];
        await Assert.That(dart).Contains("await");
    }

    [Test]
    public async Task MethodParameterDefaults_RenderInOptionalPositionalBlock_577()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public int Add(int x, int y = 5) => x + y;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var dart = files["calc.dart"];
        await Assert.That(dart).Contains("int add(int x, [int y = 5]) => x + y;");
    }

    [Test]
    public async Task ParamsArgument_WrapsExpandedElementsAsOneList_AndKeepsNormalArray_585()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Variadic
            {
                public static int Sum(int factor, params int[] xs)
                {
                    var total = 0;
                    foreach (var x in xs) total += x;
                    return total * factor;
                }

                public static int Sum(string s) => s.Length;

                public static int CallExpanded() => Sum(2, 1, 2, 3);

                public static int CallEmpty() => Sum(2);

                public static int CallNormal(int[] arr) => Sum(2, arr);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var dart = files["variadic.dart"];
        await Assert.That(dart).Contains("Variadic.sumFactorXs(2, [1, 2, 3])");
        await Assert.That(dart).Contains("Variadic.sumFactorXs(2, [])");
        await Assert.That(dart).Contains("Variadic.sumFactorXs(2, arr)");
    }

    [Test]
    public async Task GenericMethodCall_KeepsExplicitTypeArguments_587()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public static class Generics
            {
                public static T Identity<T>(T value) => value;

                public static object Use() => Identity<object>(5);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var dart = files["generics.dart"];
        await Assert.That(dart).Contains("Generics.identity<Object>(5)");
    }

    [Test]
    public async Task DictionaryDerivedSpreadOperands_SpreadEntries_589()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class MyDict : Dictionary<string, int> { }

            [Transpile]
            public static class Spreads
            {
                public static Dictionary<string, int> CloneSub(MyDict d) => new Dictionary<string, int>([.. d]);

                public static Dictionary<string, int> CloneGeneric<T>(T d) where T : Dictionary<string, int> =>
                    new Dictionary<string, int>([.. d]);
            }
            """
        );

        await Assert.That(diagnostics.Single().Code).IsEqualTo("MS0001");
        var dart = files["spreads.dart"];
        await Assert.That(dart).Contains("...d.entries");
    }

    [Test]
    public async Task CapturedPrimaryConstructorField_IsAssignedBeforeUse_387()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Service(int Factor)
            {
                public int Scale(int v) => v * Factor;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var dart = files["service.dart"];
        await Assert.That(dart).Contains("late final int _factor;");
        await Assert.That(dart).Contains("this._factor = factor;");
    }

    [Test]
    public async Task GeneratedDart_PassesDartAnalyzeAndDartRun()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            [Transpile]
            public class Person
            {
                public string Name { get; set; }

                public Person(string name) => Name = name;
            }

            [Transpile]
            public class Service(int Factor)
            {
                public int Scale(int v) => v * Factor;
            }

            [Transpile]
            public class Calc
            {
                public int Add(int x, int y = 5) => x + y;
            }

            [Transpile]
            public class Cell<T>
            {
                public T V;

                public Cell(T v) => V = v;
            }

            [Transpile]
            public class Acc
            {
                public int Pair(int a, int b) => a * 10 + b;
            }

            [Transpile]
            public static class Program
            {
                public static List<string> Log = [];

                public static int Step(string tag, int value)
                {
                    Log.Add(tag);
                    return value;
                }

                public static int Combine(int a, int b) => a * 10 + b;

                public static int Sum(int factor, params int[] xs)
                {
                    var Total = 0;
                    foreach (var Item in xs) Total += Item;
                    return Total * factor;
                }

                public static T Identity<T>(T value) => value;

                public static Cell<T> Make<T>(T a, T b) => new Cell<T>(a);

                public static Dictionary<string, int> CloneDict<T>(T d) where T : Dictionary<string, int> =>
                    new Dictionary<string, int>([.. d]);

                public static async Task<int> GetValueAsync() => 41;

                public static async Task<int> SequenceWithAwaitAsync()
                {
                    var x = 0;
                    return (x = await GetValueAsync(), x + 1).Item2;
                }

                public static async Task<bool> Check()
                {
                    var s = "$&";
                    var p = new Person("");
                    var match = p switch { Person { Name: { Length: 0 } } => true, _ => false };
                    var svc = new Service(3);
                    var calc = new Calc();
                    var combined = Combine(b: Step("b", 2), a: Step("a", 1));
                    var summed = Sum(2, 1, 2, 3);
                    var id = Identity<object>(5);
                    object cell = Make(b: Step("cell-b", 2), a: Step("cell-a", 1));
                    var acc = new Acc();
                    var pairTotal = 0;
                    pairTotal += acc.Pair(b: Step("pair-b", 2), a: Step("pair-a", 1));
                    var d = new Dictionary<string, int>();
                    d["k"] = 7;
                    var cloned = CloneDict(d);
                    var seq = await SequenceWithAwaitAsync();
                    return s.Length == 2
                        && match
                        && svc.Scale(4) == 12
                        && calc.Add(3) == 8
                        && combined == 12
                        && cell is Cell<int>
                        && pairTotal == 12
                        && Log.Count == 6
                        && Log[0] == "b"
                        && Log[1] == "a"
                        && Log[2] == "cell-b"
                        && Log[3] == "cell-a"
                        && Log[4] == "pair-b"
                        && Log[5] == "pair-a"
                        && summed == 12
                        && (int)id == 5
                        && cloned["k"] == 7
                        && seq == 42;
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        var output = await DartToolchain.AnalyzeAndRunAsync(
            files,
            """
            import 'program.dart';

            Future<void> main() async {
              if (!await Program.check()) {
                throw StateError('Program.check() returned false');
              }
              print('OK');
            }
            """
        );

        await Assert.That(output).IsEqualTo("OK");
    }

    [Test]
    public async Task ReorderedCall_TypesClosureParametersAndExplicitTypeArguments_586()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Cell<T>
            {
                public T V;

                public Cell(T v) => V = v;
            }

            [Transpile]
            public class Acc
            {
                public int Pair(int a, int b) => a * 10 + b;
            }

            [Transpile]
            public static class Gens
            {
                public static int Tr(int x) => x;

                public static Cell<T> Make<T>(T a, T b) => new Cell<T>(a);

                public static bool Check()
                {
                    object o = Make(b: Tr(2), a: Tr(1));
                    var acc = new Acc();
                    int total = 0;
                    total += acc.Pair(b: Tr(2), a: Tr(1));
                    return o is Cell<int> && total == 12;
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        var dart = files["gens.dart"];

        await Assert
            .That(dart)
            .Contains("(int __arg1, int __arg0) => Gens.make<int>(__arg0, __arg1)");

        await Assert
            .That(dart)
            .Contains("(Acc __target, int __arg1, int __arg0) => __target.pair(__arg0, __arg1)");
    }

    [Test]
    public async Task SkippedNullableOpaqueDefault_ReportsMS0001_586_577()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Ops
            {
                public ulong? U(ulong? a = 10, int b = 0) => a;

                public bool Check() => U(b: 1) == 10;
            }
            """
        );

        await Assert.That(diagnostics).IsNotEmpty();
        await Assert.That(diagnostics.Any(d => d.Code == "MS0001")).IsTrue();
    }
}
