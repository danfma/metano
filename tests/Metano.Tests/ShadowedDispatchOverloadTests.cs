using System.Diagnostics;
using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// #778: a dispatcher branch whose runtime test repeats an earlier branch's
/// can never run, so the dispatcher drops it (and its signature) and the
/// overload is reported at its declaration with the fast path hand-written
/// TypeScript must call. The fast path itself stays.
/// </summary>
public class ShadowedDispatchOverloadTests
{
    private const string Source = """
        using System;
        using Metano.Interop;

        [Transpile]
        public static class Reactivity
        {
            public static T Root<T>(Func<Action, T> body) => body(() => { });
            public static void Root(Action<Action> body) => body(() => { });
            public static int Length(JsArray<int> items) => 1;
            public static int Length(int[] items) => 2;
        }
        """;

    private const string ExpectedReactivity = """
        export class Reactivity {
          constructor() { }

          static rootFuncOfActionT<T>(body: (arg: () => void) => T): T {
            return body(() => {
            });
          }

          static rootActionOfAction(body: (obj: () => void) => void): void {
            body(() => {
            });
          }

          static root<T>(body: (arg: () => void) => T): T;
          static root(...args: unknown[]): any {
            if (args.length === 1 && typeof args[0] === "function") {
              return Reactivity.rootFuncOfActionT(args[0] as any);
            }

            throw new Error("No matching overload for root");
          }

          static lengthJsArrayOfInt32(items: number[]): number {
            return 1;
          }

          static lengthInt32Array(items: number[]): number {
            return 2;
          }

          static length(items: number[]): number;
          static length(...args: unknown[]): number {
            if (args.length === 1 && Array.isArray(args[0])) {
              return Reactivity.lengthJsArrayOfInt32(args[0] as number[]);
            }

            throw new Error("No matching overload for length");
          }
        }

        """;

    [Test]
    public async Task ShadowedBranches_AreDropped()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert.That(files["reactivity.ts"]).IsEqualTo(ExpectedReactivity);
    }

    [Test]
    public async Task ShadowedOverloads_WarnNamingTheFastPath()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        var messages = diagnostics
            .Where(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(messages.Count).IsEqualTo(2);

        await Assert
            .That(messages[0])
            .IsEqualTo(
                "Overload 'Reactivity.Root(Action<Action>)' has the same runtime test as "
                    + "'Reactivity.Root<T>(Func<Action, T>)' in the TypeScript dispatcher 'root', "
                    + "which therefore leaves it out. C# call sites bind to it directly; "
                    + "hand-written TypeScript must call 'Reactivity.rootActionOfAction'."
            );

        await Assert
            .That(messages[1])
            .IsEqualTo(
                "Overload 'Reactivity.Length(int[])' has the same runtime test as "
                    + "'Reactivity.Length(JsArray<int>)' in the TypeScript dispatcher 'length', "
                    + "which therefore leaves it out. C# call sites bind to it directly; "
                    + "hand-written TypeScript must call 'Reactivity.lengthInt32Array'."
            );
    }

    [Test]
    public async Task ShadowedBranches_TypeCheckStrictly()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(Source);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task InstanceShadowedOverload_NamesTheFastPathWithoutReceiver()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Picker
            {
                public int Pick(System.Func<int, int> f) => f(1);
                public int Pick(System.Func<string, int> f) => 2;
            }
            """
        );

        var message = diagnostics
            .Single(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .Message;

        await Assert.That(message).EndsWith("must call 'pickFuncOfStringInt32'.");
        await Assert.That(files["picker.ts"]).DoesNotContain("this.pickFuncOfStringInt32(args");
        await Assert.That(files["picker.ts"]).Contains("pickFuncOfStringInt32(f:");
    }

    [Test]
    public async Task ModuleFunctionShadowedOverload_IsDroppedAndReported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            public static class Sums
            {
                public static int Sum(int[] xs) => 1;
                public static int Sum(double[] xs) => 2;
            }
            """
        );

        var message = diagnostics
            .Single(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .Message;

        await Assert.That(message).EndsWith("must call 'sumDoubleArray'.");
        await Assert.That(files["sums.ts"]).DoesNotContain("return sumDoubleArray(args");
        await Assert.That(files["sums.ts"]).Contains("export function sumDoubleArray(");
    }

    /// <summary>
    /// The delegate test is <c>typeof === "function"</c> alone:
    /// <c>Function.length</c> leaves out ignored parameters, so a zero-arity
    /// callback reaches the value-returning overload instead of throwing
    /// "No matching overload" (#778, Metanui audit).
    /// </summary>
    [Test]
    public async Task ZeroArityCallback_ReachesTheValueReturningOverload()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public static class Probes
            {
                public static void Run(Action<int> body) => body(1);
                public static T Run<T>(Func<int, T> body) => body(2);
            }
            """
        );

        var output = files["probes.ts"];

        await Assert.That(output).DoesNotContain("args[0].length");
        await Assert.That(output).Contains("return Probes.runFuncOfInt32T(args[0] as any);");

        var driver = """
            import { Probes } from "./probes.ts";

            const value: number = Probes.run(() => 42);
            let seen = 0;
            Probes.run((n: number) => { seen = n; });
            console.log(`${value},${seen}`);
            """;

        await Assert.That(await RunWithBun(output, driver)).IsEqualTo("42,2");
    }

    private static async Task<string> RunWithBun(string probes, string driver)
    {
        var root = Directory.CreateTempSubdirectory("metano-778-").FullName;

        try
        {
            File.WriteAllText(Path.Combine(root, "probes.ts"), probes);
            File.WriteAllText(Path.Combine(root, "driver.ts"), driver);

            var start = new ProcessStartInfo("bun", ["driver.ts"])
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("bun did not start");

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);

            return ((await output) + (await error)).Trim();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The constructor dispatcher follows the same delegate rules: different
    /// arities stay apart by minimum length, one arity keeps the overload
    /// whose delegate returns a value and reports the other's factory.
    /// </summary>
    [Test]
    public async Task ConstructorDispatcher_DelegateOverloads()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public class Wide
            {
                public int K;
                public Wide(Action<int> a) { K = 1; }
                public Wide(Action<int, int> a) { K = 2; }
            }

            [Transpile]
            public class Same
            {
                public int K;
                public Same(Action<int> a) { K = 1; }
                public Same(Func<int, int> a) { K = 2; }
            }
            """
        );

        await Assert
            .That(files["wide.ts"])
            .Contains(
                "if (args.length === 1 && typeof args[0] === \"function\" && args[0].length >= 2) {"
            );

        await Assert
            .That(files["wide.ts"])
            .Contains("if (args.length === 1 && typeof args[0] === \"function\") {");

        var same = files["same.ts"];

        await Assert.That(same).Contains("  constructor(a: (arg: number) => number);");
        await Assert.That(same).DoesNotContain("  constructor(a: (obj: number) => void);");

        var message = diagnostics
            .Single(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .Message;

        await Assert.That(message).StartsWith("Overload 'Same.Same(Action<int>)'");
        await Assert.That(message).EndsWith("must call 'Same.fromActionOfInt32'.");
    }
}
