using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// #799 (spec 013 FR-016, FR-019 as W10-8a amends them): a dispatcher left
/// with one reachable overload is a typed forwarder, and a <c>[Name]</c> pin on
/// an overload the dispatcher drops (MS0063) is an acknowledged choice: a
/// metadata-only consumer binds to its fast path or factory, so it is not
/// reported.
/// </summary>
public class PinnedShadowedOverloadTests
{
    private const string Library = """
        using System;
        using Metano.Annotations;
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("acme-lib")]

        namespace Acme
        {
            public class Reactivity
            {
                public static T Root<T>(Func<Action, T> body) => body(() => { });

                [Name("rootVoid")]
                public static void Root(Action<Action> body) => Root<bool>(d => { body(d); return true; });
            }

            public class Picker
            {
                public string Seen = "";

                public Picker() { }

                [Name("listener")]
                public Picker(Action<int> listener) { Seen = "listener"; }

                public Picker(Func<int, int> map) { Seen = "map"; }

                public virtual string Pick(Func<int, int> f, int x = 3) => "fx:" + f(x);

                [Name("text")]
                public virtual string Pick(Func<string, int> f, int x = 4) => "text:" + f("ab");
            }
        }
        """;

    private const string Consumer = """
        using System;
        [assembly: TranspileAssembly]

        namespace App
        {
            public class Fancy : Acme.Picker
            {
                public Fancy() : base((int v) => { }) { }
            }

            public static class Use
            {
                public static string Run()
                {
                    Acme.Reactivity.Root(d => { });
                    var n = Acme.Reactivity.Root<int>(d => 1);
                    var p = new Acme.Picker((int a) => { });
                    return p.Pick((string s) => s.Length) + p.Pick((int v) => v) + n;
                }
            }
        }
        """;

    private const string ExpectedConsumerUse = """
        import { Picker, Reactivity } from "acme-lib/acme";

        export class Use {
          private constructor() { }

          static run(): string {
            Reactivity.rootRootVoid((d: () => void) => {
            });
            const n = Reactivity.root<number>((d: () => void) => 1);
            const p = Picker.fromListener((a: number) => {
            });

            return (p.pickText((s: string) => s.length, 4) ?? "") + (p.pick((v: number) => v) ?? "") + n;
          }
        }

        """;

    [Test]
    public async Task PinnedShadowedOverloads_AreNotReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Library);

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch))
            .IsEmpty();
    }

    /// <summary>
    /// A module function is reported even when pinned: a consumer in another
    /// package keeps calling its dispatcher, so the message suggests no pin.
    /// </summary>
    [Test]
    public async Task PinnedShadowedModuleFunction_IsStillReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            public static class Sums
            {
                public static int Sum(int[] xs) => 1;

                [Name("doubles")]
                public static int Sum(double[] xs) => 2;
            }
            """
        );

        var message = diagnostics
            .Single(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .Message;

        await Assert.That(message).EndsWith("must call 'sumDoubles'.");
    }

    [Test]
    public async Task LoneReachableOverload_EmitsATypedForwarder()
    {
        var files = TranspileHelper.Transpile(Library);

        var reactivity = files["acme/reactivity.ts"];
        var picker = files["acme/picker.ts"];

        await Assert
            .That(reactivity)
            .Contains(
                "  static root<T>(body: (arg: () => void) => T): T {\n"
                    + "    return Reactivity.rootBody<T>(body);\n"
                    + "  }\n"
            );

        await Assert
            .That(picker)
            .Contains(
                "  pick(f: (arg: number) => number, x?: number): string {\n"
                    + "    return this.pickFX(f, x);\n"
                    + "  }\n"
            );

        await Assert.That(reactivity).DoesNotContain("...args");
        await Assert.That(picker).DoesNotContain("No matching overload for pick");
    }

    /// <summary>
    /// A forwarder keeps <c>params</c> as a rest parameter, the lone overload's
    /// accessibility and tag, and a return-only type parameter bound; a module
    /// group forwards the same way.
    /// </summary>
    [Test]
    public async Task TypedForwarders_KeepRestParametersAccessibilityAndTypeArguments()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public class Shapes
            {
                internal int Count(Action<int> a, params int[] rest) => rest.Length;

                internal int Count(Action<string> a, params int[] rest) => -1;

                public T Make<T>(Func<int, T> f) => f(1);

                public void Make(Action<int> f) => f(2);
            }

            [Transpile, ExportedAsModule]
            public static class Sums
            {
                public static int Sum(int[] xs) => 1;

                public static int Sum(double[] xs) => 2;
            }
            """
        );

        await Assert
            .That(files["shapes.ts"])
            .Contains(
                "  /** @internal */\n"
                    + "  count(a: (obj: number) => void, ...rest: number[]): number {\n"
                    + "    return this.countActionOfInt32Int32Array(a, ...rest);\n"
            );

        await Assert.That(files["shapes.ts"]).Contains("    return this.makeFuncOfInt32T<T>(f);\n");

        await Assert
            .That(files["sums.ts"])
            .Contains(
                "export function sum(xs: number[]): number {\n"
                    + "  return sumInt32Array(xs);\n"
                    + "}\n"
            );

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    /// <summary>
    /// The forwarder calls the fast path, so an override of the lone overload
    /// runs through it, and a derived type that adds an overload overrides the
    /// forwarder with a dispatcher that sends the others to it.
    /// </summary>
    [Test]
    public async Task Forwarder_KeepsOverridesAndDerivedDispatchers()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;

            [Transpile, EmitInFile("probes")]
            public class Base
            {
                public virtual string Pick(Func<int, int> f, int x = 3) => "base:" + f(x);

                public virtual string Pick(Func<string, int> f, int x = 4) => "text";
            }

            [Transpile, EmitInFile("probes")]
            public class Over : Base
            {
                public override string Pick(Func<int, int> f, int x = 3) => "over:" + f(x);
            }

            [Transpile, EmitInFile("probes")]
            public class Wide : Base
            {
                public string Pick(string s) => "wide:" + s;
            }
            """
        );

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);

        var driver = """
            import { Base, Over, Wide } from "./probes.ts";

            const twice = (v: number) => v * 2;
            console.log([
              new Base().pick(twice),
              new Over().pick(twice),
              new Wide().pick(twice, 5),
              new Wide().pick("s"),
            ].join(","));
            """;

        await Assert
            .That(await ShadowedDispatchOverloadTests.RunWithBun(files["probes.ts"], driver))
            .IsEqualTo("base:6,over:6,base:10,wide:s");
    }

    /// <summary>
    /// A consumer that reads the producer from metadata only binds the calls to
    /// pinned overloads: a static and an instance fast path, a factory, and a
    /// <c>: base(...)</c> selector. Unpinned overloads keep the dispatcher.
    /// </summary>
    [Test]
    public async Task MetadataOnlyConsumer_BindsPinnedOverloads()
    {
        var files = TranspileHelper.TranspileWithLibraryImage(Library, Consumer);

        await Assert.That(files["app/use.ts"]).IsEqualTo(ExpectedConsumerUse);

        await Assert
            .That(files["app/fancy.ts"])
            .Contains("super(Picker.byPickerListener, (v: number) => {");
    }

    /// <summary>
    /// A reference assembly strips private members, not the pins: the consumer
    /// names the same members, and the pair type-checks under
    /// <c>tsgo --strict</c>.
    /// </summary>
    [Test]
    public async Task ReferenceAssemblyConsumer_BindsPinnedOverloads_AndTypeChecks()
    {
        var consumer = TranspileHelper.TranspileWithLibraryImage(
            Library,
            Consumer,
            referenceAssembly: true
        );

        await Assert.That(consumer["app/use.ts"]).IsEqualTo(ExpectedConsumerUse);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            TranspileHelper.Transpile(Library),
            consumer
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    private const string ClashSource = """
        using Metano.Annotations;

        [Transpile]
        public class Clash5
        {
            public string Seen = "";

            public Clash5(int n) { Seen = "int"; }

            [Name("label")]
            public Clash5(string s) { Seen = "label"; }

            internal static Clash5 FromLabel(string s) => new Clash5(99);

            internal string PickText(string g) => "internal:" + g;

            public string Pick(int n) => "c5t:" + n;

            [Name("text")]
            public string Pick(string s) => "c5t:" + s;
        }

        [Transpile]
        public class Clash2
        {
            private string PickText(string g) => "private:" + g;

            public string Pick(int n) => "c2:" + n;

            [Name("text")]
            public string Pick(string s) => "c2t:" + s;
        }
        """;

    /// <summary>
    /// W10-8c (#799 review): a pinned fast path or factory never takes the
    /// <c>$</c> rename, since a metadata-only consumer calls it without seeing an
    /// <c>internal</c> or <c>private</c> claimant. The collision is an
    /// <c>MS0068</c> error at the member holding the name.
    /// </summary>
    [Test]
    public async Task PinnedNameCollision_ReportsMS0068AtTheHolder()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ClashSource);

        var errors = diagnostics
            .Where(d => d.Code == DiagnosticCodes.OverloadFastPathHijacked)
            .OrderBy(d => d.Location?.GetLineSpan().StartLinePosition.Line)
            .Select(d =>
                $"// {d.Code} {d.Severity} line {d.Location?.GetLineSpan().StartLinePosition.Line + 1}: "
                + d.Message
            );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.OverloadNameShadowed))
            .IsEmpty();

        await TranspileHelper.AssertGolden(
            "overload-pinned-name-collision.ts",
            string.Join("\n", errors) + "\n\n" + files["clash5.ts"] + files["clash2.ts"]
        );
    }

    /// <summary>The Dart claim pass keeps pinned names the same way (W10-8c).</summary>
    [Test]
    public async Task DartPinnedNameCollision_ReportsMS0068AtTheHolder()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(ClashSource);

        var errors = diagnostics
            .Where(d => d.Code == DiagnosticCodes.OverloadFastPathHijacked)
            .Select(d => d.Location?.GetLineSpan().StartLinePosition.Line + 1)
            .Order()
            .ToList();

        // The Dart helper compiles the source without the TypeScript helper's
        // leading using line, so each holder is one line earlier here.
        await Assert.That(errors).IsEquivalentTo(new int?[] { 15, 17, 28 });
        await Assert.That(string.Join("\n", files.Values)).DoesNotContain("pickText$");
        await Assert.That(string.Join("\n", files.Values)).DoesNotContain("fromLabel$");
    }
}
