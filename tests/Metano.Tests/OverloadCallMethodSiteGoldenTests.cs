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
    /// The exported-module-function predicate is one definition for the
    /// whole barrel: on an extension container — a static class that keeps
    /// its namespace — a dispatched group of plain public statics claims
    /// fast paths exactly like the classic extensions beside it. Both
    /// halves judge the declaration (a reduced call-site form resolves
    /// through the same declaration keys), and a non-public overload never
    /// joins the barrel: the private <c>Fold(long)</c> shapes the group's
    /// suffixes but emits no function and claims no fast path (spec 013
    /// FR-032).
    /// </summary>
    [Test]
    public async Task ExtensionContainer_DispatchedPlainStatics_ClaimFastPathsToo()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Mixed
            {
                public static int Twice(this int x) => x * 2;

                public static int Fold(int x) => x + 1;

                public static int Fold(string s) => s.Length + 1;

                private static int Fold(long big) => (int)big;
            }
            """
        );

        var module = result["mixed.ts"];

        // The plain statics dispatch like the extension beside them: one
        // fast path per overload, named off the parameter spellings, under
        // one dispatcher whose first signature names the group.
        await Assert.That(module).Contains("export function foldX(x: number)");
        await Assert.That(module).Contains("export function foldS(s: string)");
        await Assert.That(module).Contains("export function fold(x: number): number;");

        // The single extension stays a plain module function of its own.
        await Assert.That(module).Contains("export function twice(");

        // The private overload shapes the spellings but never joins: no
        // function, no claimed fast path.
        await Assert.That(module).DoesNotContain("foldBig");
    }

    /// <summary>
    /// The MS0067 shadow report travels the extension-container path too:
    /// <c>Mixed</c> keeps its namespace and joins the barrel only through
    /// its classic extension, yet a fast path of its plain statics whose
    /// candidate name a sibling type's dispatcher took is renamed with a
    /// warning at the member, and the module emits the renamed function
    /// (spec 013 FR-032).
    /// </summary>
    [Test]
    public async Task ExtensionContainer_FastPathClash_ReportsTheShadowAndRenames()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Real
            {
                public static int FoldX(int x) => x;
            }

            [Transpile]
            public static class Mixed
            {
                public static int Twice(this int x) => x * 2;

                public static int Fold(int x) => x + 1;

                public static int Fold(string s) => s.Length + 1;
            }

            [Transpile]
            public class UseMixed
            {
                public int One() => Mixed.Fold(1);

                public int Two() => Mixed.Fold("ab");
            }
            """
        );

        var shadows = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.OverloadNameShadowed)
            .ToList();

        await Assert.That(shadows.Count).IsEqualTo(1);
        await Assert.That(shadows[0].Message).Contains("'Mixed.foldX'");
        await Assert.That(shadows[0].Location).IsNotNull();

        var module = result["mixed.ts"];

        await Assert.That(module).Contains("export function foldX$(x: number)");
        await Assert.That(module).DoesNotContain("export function foldX(x: number)");
        await Assert.That(result["use-mixed.ts"]).Contains("foldX$(1)");
        await Assert.That(result["use-mixed.ts"]).Contains("foldS(\"ab\")");
    }

    /// <summary>
    /// The barrel claims fast paths group by group, in the groups'
    /// first-declaration order, so the clean name of a cross-group collision
    /// goes to the group declared first — exactly the names the barrel has
    /// always handed out (spec 013 FR-032). <c>Get</c> is declared before
    /// <c>GetAll</c>, so <c>Get(int allX)</c> keeps <c>getAllX</c> and the
    /// first <c>GetAll</c> overload is renamed to <c>getAllX$</c>, with
    /// MS0067 raised at the <c>GetAll</c> member and every call site calling
    /// the claimed name of the overload Roslyn resolved.
    /// </summary>
    [Test]
    public async Task ModuleGroup_ClaimsGroupByGroup_PinningTheBarrelNames()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Clash
            {
                public static int Get(int a, int b) => a;

                public static int GetAll(int x) => x;

                public static int Get(int allX) => allX;

                public static int GetAll(int x, int y) => x;
            }

            [Transpile]
            public class UseClash
            {
                public int Ab() => Clash.Get(1, 2);

                public int One() => Clash.GetAll(1);

                public int Two() => Clash.Get(5);

                public int Both() => Clash.GetAll(1, 2);
            }
            """
        );

        // The clean name belongs to Get(int allX), the first group's second
        // overload; GetAll(int x) — the second group — is renamed. The
        // callers call the claimed name of the overload Roslyn resolved.
        var module = result["clash.ts"];
        await Assert.That(module).Contains("export function getAB(a: number, b: number)");
        await Assert.That(module).Contains("export function getAllX(allX: number)");
        await Assert.That(module).Contains("export function getAllX$(x: number)");
        await Assert.That(module).Contains("export function getAllXY(x: number, y: number)");

        var shadows = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.OverloadNameShadowed)
            .ToList();

        await Assert.That(shadows.Count).IsEqualTo(1);
        await Assert.That(shadows[0].Message).Contains("'Clash.getAllX'");
        await Assert.That(shadows[0].Message).Contains("of 'GetAll(int x)'");
        await Assert.That(shadows[0].Location).IsNotNull();

        // The call sites call the claimed name of the overload Roslyn
        // resolved — the renamed GetAll(int x) included.
        var calls = result["use-clash.ts"];
        await Assert.That(calls).Contains("getAB(1, 2)");
        await Assert.That(calls).Contains("getAllX(5)");
        await Assert.That(calls).Contains("getAllX$(1)");
        await Assert.That(calls).Contains("getAllXY(1, 2)");
    }

    /// <summary>
    /// A dispatched group on an ambient (<c>[NoContainer, External]</c>)
    /// class emits no fast path — <see cref="Metano.Compiler.IR.IrOverloadRef"/>
    /// stays null for a type nothing is emitted for — so the barrel reports
    /// no MS0067 even when its would-be fast path collides with a sibling's
    /// dispatcher name. The name reservation stays: pass 1 still takes the
    /// ambient dispatchers into the barrel table.
    /// </summary>
    [Test]
    public async Task ExternalContainer_ClaimsNoFastPath_EmitsNoShadowDiagnostic()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;

            [assembly: TranspileAssembly]

            [NoContainer, External]
            public static class Ambient
            {
                public static int Qux(int x) => x;

                public static int Qux(string s) => s.Length;
            }

            [NoContainer]
            public static class Real
            {
                public static int QuxX(int x) => x;
            }
            """
        );

        var shadows = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.OverloadNameShadowed)
            .ToList();

        await Assert.That(shadows).IsEmpty();

        var outputs = string.Join("\n", result.Values);

        await Assert.That(outputs).DoesNotContain("quxX$");
        await Assert.That(result["real.ts"]).Contains("export function quxX(");
    }

    /// <summary>
    /// An <c>[Import]</c> facade emits no function, yet every call to it
    /// binds its local name — <c>import { fooX } from "ext-lib"</c> — in
    /// the caller's file, so the barrel reserves that name before any fast
    /// path claims it: <c>Foo(int x)</c> and, across classes,
    /// <c>Qux(int x)</c> move to <c>fooX$</c> and <c>quxX$</c> with MS0067,
    /// and the external import survives beside them. An <c>[Emit]</c>
    /// template or an <c>[Ignore]</c>d member binds nothing, so the
    /// <c>barX</c> and <c>bazX</c> fast paths keep their clean names (spec
    /// 013 FR-032).
    /// </summary>
    [Test]
    public async Task ImportFacade_ReservesItsLocalName_BeforeTheFastPaths()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Res
            {
                [Import("fooX", "ext-lib")]
                public static int FooX(int x) => x;

                [Emit("($0 + 1)")]
                public static int BarX(int x) => x;

                [Ignore]
                public static int BazX(int x) => x;

                public static int Foo(int x) => x;

                public static int Foo(string s) => 0;

                public static int Bar(int x) => x;

                public static int Bar(string s) => 0;

                public static int Baz(int x) => x;

                public static int Baz(string s) => 0;
            }

            [NoContainer]
            public static class Ext
            {
                [Import("quxX", "ext-lib")]
                public static int QuxX(int x) => x;
            }

            [NoContainer]
            public static class Q
            {
                public static int Qux(int x) => x;

                public static int Qux(string s) => 0;
            }

            public class Use
            {
                public int Go() =>
                    Res.FooX(1) + Res.BarX(2) + Res.Foo(1) + Res.Foo("a") + Res.Bar(1)
                    + Res.Bar("b") + Res.Baz(1) + Res.Baz("c") + Ext.QuxX(5) + Q.Qux(1)
                    + Q.Qux("z");
            }
            """
        );

        var shadows = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.OverloadNameShadowed)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        await Assert.That(shadows.Count).IsEqualTo(2);
        await Assert.That(shadows[0]).Contains("'Res.fooX' of 'Foo(int x)'");
        await Assert.That(shadows[1]).Contains("'Q.quxX' of 'Qux(int x)'");

        await Assert.That(result["res.ts"]).Contains("export function fooX$(x: number)");
        await Assert.That(result["res.ts"]).Contains("export function barX(x: number)");
        await Assert.That(result["res.ts"]).Contains("export function bazX(x: number)");
        await Assert.That(result["q.ts"]).Contains("export function quxX$(x: number)");
        await Assert
            .That(result["use.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-import-facade-reserved.ts"));
    }

    /// <summary>
    /// The name an <c>[Import]</c> facade reserves is the one it binds, not
    /// the one it imports: <c>[Import("other")] OtherX</c> binds
    /// <c>otherX</c> (<c>import { other as otherX }</c>), so the
    /// <c>Other(int x)</c> fast path moves to <c>otherX$</c>, while
    /// <c>[Import("otherS")] Thing</c> binds <c>thing</c> and leaves the
    /// <c>otherS</c> fast path alone. Under an <c>[Emit]</c> template the
    /// template spells the imported name itself, so <c>fooY</c> is reserved
    /// and the <c>Foo(int y)</c> fast path moves to <c>fooY$</c> (spec 013
    /// FR-032).
    /// </summary>
    [Test]
    public async Task ImportFacade_ReservesTheBoundAlias_NotTheImportedName()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            [NoContainer]
            public static class Res
            {
                [Import("other", "ext-lib")]
                public static int OtherX(int x) => x;

                [Import("otherS", "ext-lib")]
                public static int Thing(string s) => 0;

                [Emit("fooY($0)"), Import("fooY", "ext-lib")]
                public static int Templ(int y) => y;

                public static int Other(int x) => x;

                public static int Other(string s) => 0;

                public static int Foo(int y) => y;

                public static int Foo(string s) => 0;
            }

            public class Use
            {
                public int Go() =>
                    Res.OtherX(1) + Res.Thing("t") + Res.Templ(2) + Res.Other(1) + Res.Other("a")
                    + Res.Foo(3) + Res.Foo("b");
            }
            """
        );

        var shadows = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.OverloadNameShadowed)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        await Assert.That(shadows.Count).IsEqualTo(2);
        await Assert.That(shadows[0]).Contains("'Res.otherX' of 'Other(int x)'");
        await Assert.That(shadows[1]).Contains("'Res.fooY' of 'Foo(int y)'");

        await Assert.That(result["res.ts"]).Contains("export function otherX$(x: number)");
        await Assert.That(result["res.ts"]).Contains("export function otherS(s: string)");
        await Assert.That(result["res.ts"]).Contains("export function fooY$(y: number)");
        await Assert
            .That(result["use.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-import-facade-alias.ts"));
    }

    /// <summary>
    /// A <c>[NoContainer]</c> class holding a C# 14 <c>extension(...)</c>
    /// block still exports the block's members through the extension-helper
    /// registry: the lifted static view of a block member is no module
    /// function of its own (the block emits it), and the registry resolves
    /// the block members off the block's synthetic type, so a caller in
    /// another file imports <c>twice</c> and <c>size$get</c> from the
    /// host's module (spec 013 FR-032).
    /// </summary>
    [Test]
    public async Task NoContainerExtensionBlock_ResolvesImportsThroughTheHelperRegistry()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            [NoContainer]
            public static class BlockHost
            {
                extension(string s)
                {
                    public int Twice() => s.Length * 2;

                    public int Size => s.Length;
                }

                public static int Plain(int x) => x;

                public static int Plain(string x) => 0;
            }

            public class UseBlock
            {
                public int Go() => "x".Twice() + "x".Size + BlockHost.Plain(1) + BlockHost.Plain("y");
            }
            """
        );

        await Assert
            .That(result["use-block.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("module-extension-block-imports.ts"));
    }

    /// <summary>
    /// Two containerless classes claiming the same dispatcher name keep the
    /// cross-type clash report (MS0020) the single-function path has always
    /// raised, and the barrel's fast-path claim reports MS0067 (warning, at
    /// the member) while the losing module still emits its renamed fast
    /// paths and its callers call them (spec 013 FR-004, FR-032).
    /// </summary>
    [Test]
    public async Task ModuleGroupClash_ReportsTheCollisionAndTheRenamedFastPaths()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
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

            [Transpile]
            public class Caller
            {
                public static int Direct() => Second.Twice(21);

                public static int OfString() => Second.Twice("abcd");
            }
            """
        );

        var clashes = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.NoContainerFactoryNameClash)
            .ToList();

        await Assert.That(clashes.Count).IsGreaterThan(0);
        await Assert.That(clashes[0].Message).Contains("'Second.Twice'");

        var renames = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.OverloadNameShadowed)
            .ToList();

        await Assert.That(renames.Count).IsGreaterThanOrEqualTo(2);

        foreach (var rename in renames)
        {
            await Assert.That(rename.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
            await Assert.That(rename.Location).IsNotNull();
            await Assert.That(rename.Message).Contains("'Second.");
        }

        // The losing module still emits its claimed fast paths, renamed;
        // its caller calls the claimed names.
        await Assert.That(result["second.ts"]).Contains("twiceX$");
        await Assert.That(result["second.ts"]).Contains("twiceS$");
        await Assert.That(result["caller.ts"]).Contains("twiceX$(");
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
