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

    [Test]
    public async Task ParamsForms_PassTheBoundArguments()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Extra
            {
                public static int P(int a, params int[] parts) => a + parts.Length;

                public static int Parts(params int[] parts) => parts.Length;
            }

            [Transpile]
            public class Use
            {
                public int Expanded() => Extra.P(1, 2, 3);

                public int Pair() => Extra.P(5, 6);

                public int Alone() => Extra.P(5);

                public int Spread(int[] items) => Extra.P(1, items);

                public int Empty() => Extra.Parts();

                public int Several() => Extra.Parts(1, 2, 3);

                public int Arr(int[] xs) => Extra.Parts(xs);
            }
            """
        );

        await Assert
            .That(result["use.ts"])
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
