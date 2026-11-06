using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The direct method call sites of spec 013 slice 013e (FR-018, FR-019,
/// FR-032, DECIDED-32), pinned as goldens that compile under
/// <c>tsgo --strict --noImplicitOverride --declaration</c>: a call Roslyn
/// resolved into a dispatched method group calls that overload's claimed
/// fast path — instance receiver evaluated once in place, a static on the
/// type, an extension on the imported helper, an operator on the type's
/// fast path, a method group bound to its receiver — with Roslyn's bound
/// arguments (an omitted optional passes the static overload's default
/// explicitly, an expanded <c>params</c> its elements and a normal-form one
/// the spread array), generic calls carrying their type arguments.
/// </summary>
public class OverloadCallMethodSiteGoldenTests
{
    [Test]
    public async Task MethodCall_CallsTheResolvedOverloadsFastPath()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Scale
            {
                public int Factor { get; private set; }

                public Scale() { Factor = 1; }

                public int Apply(int f) => Factor * f;

                public int Apply(int f, int d) => Factor * f / d;

                public int Apply(string mode) => mode.Length;

                public static Scale Identity() => new();

                public static Scale With(int factor) => new() { Factor = factor };
            }

            [Transpile]
            public class Caller
            {
                public int TimesTwo() => new Scale().Apply(2);

                public int Divided() => Scale.Identity().Apply(8, 4);

                public int OfWord() => new Scale().Apply("word");

                public Scale Typed(Scale s) => s.Apply(1) == 2 ? s : Scale.Identity();
            }
            """
        );

        await Assert
            .That(result["caller.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-methods.ts"));
    }

    /// <summary>
    /// DECIDED-32: the static receiver type's overload binds an omitted
    /// optional argument — a hiding <c>new</c> method's different default
    /// binds only when the static type is the derived one, exactly as C#
    /// reads it (the probe run of this source under .NET prints 3 and 90).
    /// </summary>
    [Test]
    public async Task OmittedDefaults_BindTheStaticOverloadsDefault()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class BaseGreeter
            {
                public int Greet(object other) => -1;

                public int Greet(string name, int times = 3) => times;
            }

            [Transpile]
            public class LoudGreeter : BaseGreeter
            {
                public new int Greet(string name, int times = 9) => times * 10;
            }

            [Transpile]
            public class Caller
            {
                public static int ThroughBase()
                {
                    BaseGreeter g = new LoudGreeter();
                    return g.Greet("x");
                }

                public static int Direct()
                {
                    var g = new LoudGreeter();
                    return g.Greet("x");
                }
            }
            """
        );

        await Assert
            .That(result["caller.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-defaults.ts"));
    }

    /// <summary>
    /// DECIDED-21 on a dispatched group: an expanded <c>params</c> call
    /// passes its elements, a normal-form one the spread array, an omitted
    /// optional + <c>params</c> call the bound default and nothing for the
    /// empty rest, and the other overload of each group keeps its own fast
    /// path. Also pins the null-conditional receiver on a dispatched group
    /// and the unqualified call inside the declaring class.
    /// </summary>
    [Test]
    public async Task ParamsForms_PassTheBoundArguments()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Extra
            {
                public int P(int a, params int[] rest) => a + rest.Length;

                public int P(string s) => s.Length;

                public int M(int a, int b = 2, params int[] rest) => a + b + rest.Length;

                public int M(string s) => s.Length;

                public int Unqualified(int a) => P(a, 8, 9);

                public int Conditional(Extra? other) => other?.P(1, 2) ?? -1;
            }

            [Transpile]
            public class Use
            {
                public int Expanded(Extra e) => e.P(1, 2, 3);

                public int Pair(Extra e) => e.P(5, 6);

                public int Alone(Extra e) => e.P(7);

                public int Spread(Extra e, int[] items) => e.P(1, items);

                public int OfString(Extra e) => e.P("abcd");

                public int Defaults(Extra e) => e.M(1);

                public int OneRest(Extra e) => e.M(1, 3, 4);

                public int OfStringM(Extra e) => e.M("ab");
            }
            """
        );

        var outputs = string.Join("\n", result["extra.ts"], result["use.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-params.ts"));
    }

    /// <summary>
    /// R12: C# binds on the static type — <c>object o = "s"; F(o)</c> calls
    /// <c>F(object)</c>, where the runtime dispatcher would pick
    /// <c>F(string)</c> by the value's runtime type.
    /// </summary>
    [Test]
    public async Task StaticBinding_KeepsTheStaticTypesOverload()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Binder
            {
                public string F(object a) => "object";

                public string F(string a) => "string";

                public string Pick()
                {
                    object o = "s";
                    return F(o);
                }

                public string Direct() => F("s");
            }
            """
        );

        await Assert
            .That(result["binder.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-static-binding.ts"));
    }

    [Test]
    public async Task GenericCalls_CarryTheirTypeArguments()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Shapes
            {
                public static T Identity<T>(T value) => value;

                public static string Identity(string value) => value;
            }

            [Transpile]
            public class Use
            {
                public int OfInt(int x) => Shapes.Identity<int>(x);

                public string OfWord() => Shapes.Identity<string>("w");

                public string OfString() => Shapes.Identity("s");
            }
            """
        );

        await Assert
            .That(result["use.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-generics.ts"));
    }

    [Test]
    public async Task OperatorCall_NamesTheResolvedOverloadsFastPath()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Money(int Cents)
            {
                public static Money operator +(Money a, int cents) => new(a.Cents + cents);

                public static Money operator +(Money a, Money b) => new(a.Cents + b.Cents);
            }

            [Transpile]
            public static class Ledger
            {
                public static Money AddCents(Money m) => m + 5;

                public static Money AddMoney(Money a, Money b) => a + b;
            }
            """
        );

        await Assert
            .That(result["ledger.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-operators.ts"));
    }

    /// <summary>
    /// A lifted operator application — a nullable operand C# null-propagates
    /// — keeps the lifted lowering (spec 013 FR-018): the fast path only
    /// binds the resolved, non-lifted form, since the fast path would throw
    /// on the nullable's value read where C# returns null.
    /// </summary>
    [Test]
    public async Task LiftedOperator_KeepsTheLiftedGuard()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Money(int Cents)
            {
                public static Money operator +(Money a, int cents) => new(a.Cents + cents);

                public static Money operator +(Money a, Money b) => new(a.Cents + b.Cents);
            }

            [Transpile]
            public static class Wallet
            {
                public static Money? Add(Money? left, Money right) => left + right;

                public static bool Equal(Money? left, Money? right) => left == right;
            }
            """
        );

        await Assert.That(result["wallet.ts"]).Contains("== null");
    }

    /// <summary>
    /// C# evaluates the receiver before every argument, so a call whose
    /// named arguments evaluate out of parameter order binds an impure
    /// receiver first: <c>((($recv, $arg1, $arg0) => …))(get(), g(), f())</c>
    /// runs get() before f() and g(), exactly as the source reads.
    /// </summary>
    [Test]
    public async Task ReceiverOrder_BindsAnImpureReceiverFirst()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Log
            {
                public static int F() => 1;

                public static int G() => 2;
            }

            [Transpile]
            public class Engine
            {
                public int Apply(int f, int d) => f * 10 + d;

                public int Apply(string mode) => mode.Length;
            }

            [Transpile]
            public class Caller
            {
                public Engine Get() => new Engine();

                public int Ordered() => Get().Apply(d: Log.G(), f: Log.F());

                public int Plain() => Get().Apply(1, 2);
            }
            """
        );

        await Assert
            .That(result["caller.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-receiver-order.ts"));
    }

    /// <summary>
    /// A dispatched extension group on a plain <c>[Transpile] static class</c>
    /// — no module attribute — lowers its call sites to the claimed fast
    /// paths, which the importing file must resolve: the extension-helper
    /// registry carries the dispatcher and every fast path of the group
    /// (spec 013 FR-032).
    /// </summary>
    [Test]
    public async Task ExtensionHelpers_InPlainStaticClass_ImportTheFastPaths()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Item
            {
                public string Label { get; set; } = "";
            }

            [Transpile]
            public static class Ext
            {
                public static int Twice(this Item item, int x) => x * 2;

                public static int Twice(this Item item, string x) => x.Length * 2;
            }

            [Transpile]
            public class Use
            {
                public int A(Item item) => item.Twice(1);

                public int B(Item item) => item.Twice("ab");

                public int C(Item? item) => item?.Twice(1) ?? 0;
            }
            """
        );

        var outputs = string.Join("\n", result["ext.ts"], result["use.ts"]);

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-extension-helper.ts"));
    }

    /// <summary>
    /// Two containerless classes claiming the same dispatcher name keep the
    /// cross-type clash report (MS0020) the single-function path has always
    /// raised: the dispatched-group branch of the export registry goes
    /// through the same collision check for its dispatcher and fast paths.
    /// </summary>
    [Test]
    public async Task ModuleGroupClash_ReportsTheCrossTypeCollision()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            [ExportedAsModule]
            public static class First
            {
                public static int Twice(int x) => x * 2;

                public static int Twice(string s) => s.Length * 2;
            }

            [ExportedAsModule]
            public static class Second
            {
                public static int Twice(int x) => x * 3;

                public static int Twice(string s) => s.Length * 3;
            }
            """
        );

        var clashes = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.NoContainerFactoryNameClash)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        await Assert.That(clashes.Count).IsGreaterThan(0);
        await Assert.That(clashes[0]).Contains("'Second.Twice'");
    }

    [Test]
    public async Task MethodGroup_BindsTheFastPathToItsReceiver()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public class Base
            {
                public int Add(int x) => x;

                public int Add(string s) => s.Length;
            }

            [Transpile]
            public class Use
            {
                public Func<int, int> Of(Base b) => b.Add;

                public Func<string, int> OfString(Base b) => b.Add;

                public static Func<int, int> StaticBound(Base b) => b.Add;
            }
            """
        );

        await Assert
            .That(result["use.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-calls-method-groups.ts"));
    }

    /// <summary>
    /// FR-032: an overloaded module group emits one exported fast path per
    /// overload plus one exported dispatcher, and a call site — static or
    /// classic-extension syntax — calls the resolved overload's fast path
    /// from the importing file.
    /// </summary>
    [Test]
    public async Task ModuleGroup_EmitsFastPathsAndDispatchesCallsDirectly()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            [ExportedAsModule]
            public static class Greets
            {
                public static int Twice(int x) => x * 2;

                public static int Twice(string s) => s.Length * 2;

                public static int Thrice(this int x) => x * 3;

                public static int Thrice(this string s) => s.Length * 3;
            }

            [Transpile]
            public class Caller
            {
                public static int Direct() => Greets.Twice(21);

                public static int OfString() => Greets.Twice("abcd");

                public static int AsExtension() => 7.Thrice();

                public static int OfStringAsExtension() => "abcd".Thrice();
            }
            """
        );

        var outputs = string.Join(
            "\n",
            result["greets.ts"],
            result["caller.ts"],
            result["index.ts"]
        );

        await Assert
            .That(outputs)
            .IsEqualTo(TranspileHelper.ReadExpected("overload-module-extension.ts"));
    }

    [Test]
    public async Task ProjectReference_CallsTheProducerFastPaths()
    {
        var result = TranspileHelper.TranspileWithLibrary(MethodLibrary, MethodConsumer);

        await Assert
            .That(result["app/till.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-cross-package-methods.ts"));
    }

    /// <summary>
    /// DECIDED-24: a producer read from metadata only keeps the dispatcher
    /// calls — correct, only slower — and reports nothing.
    /// </summary>
    [Test]
    public async Task MetadataOnlyReference_KeepsTheMethodDispatchers()
    {
        var result = TranspileHelper.TranspileWithLibraryImage(MethodLibrary, MethodConsumer);

        await Assert
            .That(result["app/till.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-cross-package-metadata-methods.ts"));
    }

    private const string MethodLibrary = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("lib")]

        namespace Lib;

        public class Scale
        {
            public int Factor { get; }

            public Scale() { Factor = 1; }

            public Scale(int factor) { Factor = factor; }

            public int Apply(int f) => Factor * f;

            public int Apply(int f, int d) => Factor * f / d;

            public int Apply(string mode) => mode.Length;
        }

        [ExportedAsModule]
        public static class Extras
        {
            public static int Twice(int x) => x * 2;

            public static int Twice(string s) => s.Length * 2;
        }
        """;

    private const string MethodConsumer = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        namespace App;

        public class Till : Lib.Scale
        {
            public Till() { }

            public int Ring() => Apply(2);

            public int Split() => Apply(8, 4);

            public int Words() => Apply("ring");

            public int Twice() => Lib.Extras.Twice(21);
        }
        """;
}
