using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>System.IDisposable</c> and <c>using</c> (FR-111, #800, #817): the runtime interface
/// <c>IDisposable</c> kept in heritage clauses and descriptors and tested nominally, the
/// <c>[Symbol.dispose]()</c> alias of every implementer, <c>using</c> emitted as a
/// TypeScript <c>using</c> declaration (or lowered to <c>try</c>/<c>finally</c> under the
/// <c>tryFinally</c> opt-out), and the explicit <c>IDisposable.Dispose()</c> forwarder rules.
/// </summary>
public class DisposableTests
{
    private const string Resources = """
        #nullable enable

        [Transpile]
        public sealed class Res : IDisposable
        {
            public int Count;
            public void Dispose() { Count++; }
        }

        [Transpile]
        public sealed class Handle : IDisposable
        {
            public int Count;
            void IDisposable.Dispose() { Count++; }
        }

        [Transpile]
        public struct SRes : IDisposable
        {
            public void Dispose() { }
        }

        """;

    private const string TypeQuestion = """
        [Transpile]
        public static class Probe
        {
            public static bool Is(object o) => o is IDisposable;
        }
        """;

    private const string UsingForms = """
        [Transpile]
        public static class Uses
        {
            public static Res Open() => new Res();
            public static void Touch(Res? r) { }
            public static int A() { using var r = new Res(); return r.Count; }
            public static void B(Res? maybe) { using (var r = maybe) { Touch(r); } Touch(null); }
            public static void C() { using (Open()) { Touch(null); } }
            public static void D() { using (Res a = new Res(), b = new Res()) { a.Count++; } }
            public static void E() { using var a = new Res(); using var b = new Handle(); Touch(a); }
            public static void F(SRes s, SRes? ns) { using (s) { } using (ns) { } }
            public static void G<T>(T t) where T : IDisposable { using (t) { } }
            public static void K() { using (var a = new Res()) using (var b = new Res()) { a.Count++; } }
        }
        """;

    private const string NestedScopes = """
        [Transpile]
        public static class Uses
        {
            public static void Touch(Res? r) { }

            public static void I(bool f)
            {
                if (f) { using var r = new Res(); Touch(r); }

                for (var i = 0; i < 2; i++)
                {
                    using var q = new Res();
                    if (i == 0) continue;
                    break;
                }
            }

            public static int J(System.Collections.Generic.Dictionary<string, int> d)
            {
                d.TryGetValue("a", out var v);
                using var r = new Res();
                Touch(r);
                return v + 1;
            }

            public static Func<int> L() => () => { using var r = new Res(); return r.Count; };
        }
        """;

    private const string AsyncUsing = """
        [Transpile]
        public static class Uses
        {
            public static async System.Threading.Tasks.Task<int> Run(
                System.Threading.Tasks.Task<int> pending
            )
            {
                using var r = new Res();

                return await pending;
            }
        }
        """;

    [Test]
    public async Task Implementer_KeepsTheInterfaceInImplementsAndItsDescriptor()
    {
        var result = TranspileHelper.Transpile(Resources + TypeQuestion);

        await TranspileHelper.AssertGolden("disposable-implementer.ts", result["res.ts"]);
        await TranspileHelper.AssertGolden("disposable-explicit.ts", result["handle.ts"]);
    }

    [Test]
    public async Task DisposeAlias_IsDeclaredWhereTheContractIsTakenOn()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IScope : IDisposable { }

            [Transpile]
            public class Scope : IScope
            {
                public void Dispose() { }
            }

            [Transpile]
            public class ChildScope : Scope { }

            [Transpile]
            public class Overloaded : IDisposable
            {
                public void Dispose() { }
                public void Dispose(int count) { }
            }
            """
        );

        const string Alias = "[Symbol.dispose](): void {";

        await Assert.That(result["scope.ts"]).Contains(Alias);
        await Assert.That(result["scope.ts"]).Contains("disposeResource(this, this.dispose);");
        await Assert.That(result["scope.ts"]).Contains("} from \"metano-runtime\";");
        await Assert.That(result["child-scope.ts"]).DoesNotContain(Alias);
        await Assert.That(result["i-scope.ts"]).DoesNotContain(Alias);
        await Assert.That(result["overloaded.ts"]).Contains("disposeResource(this, this.dispose);");
    }

    [Test]
    public async Task DisposeAlias_CallsTheEmittedNameOfARenamedDispose()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Named : IDisposable
            {
                [Name("close")]
                public void Dispose() { }
            }
            """
        );

        await Assert.That(result["named.ts"]).Contains("close(): void {");
        await Assert.That(result["named.ts"]).Contains("disposeResource(this, this.close);");
    }

    [Test]
    public async Task UsingStatementInASwitchSection_IsABlockWithAUsingDeclaration()
    {
        var result = TranspileHelper.Transpile(
            Resources
                + """
                [Transpile]
                public static class Uses
                {
                    public static void Touch(Res? r) { }

                    public static void Run(int k)
                    {
                        switch (k)
                        {
                            case 1:
                                var shared = 1;
                                using (var r = new Res())
                                    Touch(r);
                                break;
                            case 2:
                                shared = 2;
                                using (var s = new Res()) { Touch(s); }
                                break;
                        }
                    }
                }
                """
        );

        var output = result["uses.ts"];

        // C# rejects a using declaration directly in a section (CS8647); a using statement
        // becomes a block, where a TypeScript `using` is allowed.
        var lines = output.Split('\n').Select(line => line.Trim()).ToList();
        var resource = lines.IndexOf("using r = new Res();");

        await Assert.That(resource).IsGreaterThan(0);
        await Assert.That(lines[resource - 1]).IsEqualTo("{");
        await Assert.That(output).Contains("using s = new Res();");
    }

    [Test]
    public async Task ImportedClassResource_ReportsMS0001AndIsNeverAUsingDeclaration()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Resources
                + """
                [Import("Socket", from: "net-lib")]
                public sealed class Socket : IDisposable
                {
                    public void Dispose() { }
                }

                [Transpile]
                public static class Uses
                {
                    public static Socket Open() => null!;

                    public static void Run(IDisposable handle)
                    {
                        using var socket = Open();
                        using var viaInterface = handle;
                        using var own = new Res();
                    }
                }
                """
        );

        var output = files["uses.ts"];

        // An [Import] class's Dispose is not emitted: MS0001 as in 3.3.0, the value is kept
        // undisposed; interface and own-class resources stay using declarations.
        await Assert.That(output).Contains("const socket = Uses.open();");
        await Assert.That(output).DoesNotContain("using socket");

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature && d.Message.Contains("Socket")
                )
            )
            .IsTrue();

        await Assert.That(output).Contains("using viaInterface = handle;");
        await Assert.That(output).Contains("using own = new Res();");
    }

    [Test]
    public async Task ImportedDisposableClass_WarnsOnlyForNativeUsing()
    {
        const string Source = """
            [Import("Socket", from: "net-lib")]
            public sealed class Socket : IDisposable
            {
                public void Dispose() { }
            }

            [Transpile]
            public sealed class Res : IDisposable
            {
                public void Dispose() { }
            }
            """;

        var (_, native) = TranspileHelper.TranspileWithDiagnostics(Source);
        var (_, tryFinally) = TranspileHelper.TranspileWithTryFinallyUsings(Source);

        static bool NamesSocket(MetanoDiagnostic d) =>
            d
                is {
                    Code: DiagnosticCodes.UnsupportedFeature,
                    Severity: MetanoDiagnosticSeverity.Warning
                }
            && d.Message.Contains("'Socket' is an [Import] class that implements IDisposable");

        await Assert.That(native.Count(NamesSocket)).IsEqualTo(1);
        await Assert.That(native.Any(d => d.Message.Contains("'Res'"))).IsFalse();
        await Assert.That(tryFinally.Any(NamesSocket)).IsFalse();
    }

    [Test]
    public async Task EmitDispose_GetsNoAliasReportsMS0001AndKeepsTheTryFinally()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public sealed class Handle : IDisposable
            {
                [Emit("release($0)")]
                public void Dispose() { }
            }

            [Transpile]
            public static class Uses
            {
                public static void Run() { using var h = new Handle(); }
            }
            """
        );

        await Assert.That(files["handle.ts"]).DoesNotContain("[Symbol.dispose]");
        // The class cannot satisfy IDisposable (MS0001 error below); the using keeps the
        // try/finally, whose dispose call is the 3.3.0 one (the template is not expanded).
        await Assert.That(files["uses.ts"]).DoesNotContain("using h");
        await Assert.That(files["uses.ts"]).Contains("h.dispose();");

        await Assert
            .That(
                diagnostics.Any(d =>
                    d
                        is {
                            Code: DiagnosticCodes.UnsupportedFeature,
                            Severity: MetanoDiagnosticSeverity.Error
                        }
                    && d.Message.Contains("is [Emit]")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task ImportedClassResource_UnderTranspileAssembly_KeepsTheTryFinally()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            [Import("Socket", from: "net-lib")]
            public sealed class Socket : IDisposable
            {
                public void Dispose() { }
            }

            public static class Uses
            {
                public static Socket Open() => null!;

                public static void Run()
                {
                    using var socket = Open();
                }
            }
            """
        );

        var output = result["uses.ts"];

        // The [Import] class is transpilable under TranspileAssembly, so its Dispose counts as
        // emitted, but the bound declaration has no [Symbol.dispose](): try/finally.
        await Assert.That(output).DoesNotContain("using socket");
        await Assert.That(output).Contains("socket?.dispose();");
    }

    [Test]
    public async Task ClassConstrainedTypeParameterOfAReferencedPackage_KeepsTheTryFinally()
    {
        var files = TranspileHelper.TranspileWithLibrary(
            """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace Lib;

            public class LibRes : IDisposable
            {
                public void Dispose() { }
            }
            """,
            """
            using Lib;

            [Transpile]
            public class OwnRes : IDisposable
            {
                public void Dispose() { }
            }

            [Transpile]
            public static class Uses
            {
                public static void Lib<T>(T t) where T : LibRes { using (t) { } }
                public static void Own<T>(T t) where T : OwnRes { using (t) { } }
                public static void Iface<T>(T t) where T : IDisposable { using (t) { } }
            }
            """
        );

        var output = files["uses.ts"];

        // Lib<T> keeps try/finally; Own<T> and Iface<T> declare `using` (one temporary each).
        await Assert.That(output).Contains("$using0?.dispose();");
        await Assert.That(output.Split("using $using0 = t;").Length - 1).IsEqualTo(2);
    }

    [Test]
    public async Task DisposeAlias_IsKeptUnderTheTryFinallyOptOut()
    {
        var (files, _) = TranspileHelper.TranspileWithTryFinallyUsings(Resources + TypeQuestion);

        await TranspileHelper.AssertGolden("disposable-implementer.ts", files["res.ts"]);
    }

    [Test]
    public async Task InterfaceExtendingIDisposable_KeepsItInExtends()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IScope : IDisposable
            {
                void Work();
            }
            """
        );

        var output = result["i-scope.ts"];

        await Assert.That(output).Contains("import type { IDisposable } from \"metano-runtime\";");
        await Assert.That(output).Contains("export interface IScope extends IDisposable {");
    }

    [Test]
    public async Task InterfaceAddingAnOverload_RedeclaresTheInheritedSignatures()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IClose : IDisposable
            {
                void Dispose(bool hard);
            }

            [Transpile]
            public interface IStop
            {
                void Stop();
            }

            [Transpile]
            public interface IStop2 : IStop
            {
                void Stop(bool hard);
            }

            [Transpile]
            public interface IStop3 : IStop
            {
                new void Stop();
            }
            """
        );

        await Assert
            .That(result["i-close.ts"])
            .Contains(
                """
                export interface IClose extends IDisposable {
                  dispose(hard: boolean): void;
                  dispose(): void;
                }
                """
            );

        await Assert
            .That(result["i-stop2.ts"])
            .Contains(
                """
                export interface IStop2 extends IStop {
                  stop(hard: boolean): void;
                  stop(): void;
                }
                """
            );

        await Assert
            .That(result["i-stop3.ts"])
            .Contains(
                """
                export interface IStop3 extends IStop {
                  stop(): void;
                }
                """
            );
    }

    [Test]
    public async Task TypeQuestions_TestTheRuntimeDescriptor()
    {
        var result = TranspileHelper.Transpile(
            Resources
                + """
                [Transpile]
                public static class Probe
                {
                    public static bool Is(object o) => o is IDisposable;
                    public static void Pat(object o) { if (o is IDisposable d) d.Dispose(); }
                    public static IDisposable? As(object o) => o as IDisposable;
                    public static Type Of() => typeof(IDisposable);
                    public static string M(IDisposable d) => "d";
                    public static string M(string s) => "s";
                    public static void Var() { IDisposable h = new Res(); h.Dispose(); }
                }
                """
        );

        var output = result["probe.ts"];

        await Assert.That(output).Contains("return isType(o, IDisposable$runtimeType);");

        await Assert
            .That(output)
            .Contains("if (isType(o, IDisposable$runtimeType) && (d = o, true))");

        await Assert.That(output).Contains("return asType(o, IDisposable$runtimeType);");
        await Assert.That(output).Contains("return IDisposable$runtimeType;");

        await Assert
            .That(output)
            .Contains("args.length === 1 && isType(args[0], IDisposable$runtimeType)");

        await Assert.That(output).Contains("h.dispose();");
        await Assert.That(output).DoesNotContain("Unsupported");

        await Assert
            .That(result["handle.ts"])
            .Contains("interfaces: [\n        () => IDisposable$runtimeType,");
    }

    [Test]
    public async Task UsingForms_EmitUsingDeclarations()
    {
        var result = TranspileHelper.Transpile(Resources + UsingForms);

        await TranspileHelper.AssertGolden("disposable-using-declarations.ts", result["uses.ts"]);
    }

    [Test]
    public async Task UsingForms_UnderTheTryFinallyOptOut_KeepThe330Lowering()
    {
        var (files, _) = TranspileHelper.TranspileWithTryFinallyUsings(Resources + UsingForms);

        await TranspileHelper.AssertGolden("disposable-using-forms.ts", files["uses.ts"]);
    }

    [Test]
    public async Task PatternBasedRefStruct_KeepsTheTryFinallyLowering()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public ref struct Lease
            {
                public int Count;
                public void Dispose() { Count++; }
            }

            [Transpile]
            public static class Uses
            {
                public static void Run() { using var lease = new Lease(); }
            }
            """
        );

        var output = result["uses.ts"];

        await Assert.That(output).DoesNotContain("using lease");
        await Assert.That(output).Contains("lease.dispose();");
    }

    [Test]
    public async Task UsingDeclaration_InNestedScopesLoopsAndLambdas_StaysADeclaration()
    {
        var result = TranspileHelper.Transpile(Resources + NestedScopes);
        var output = result["uses.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    if (f) {
                      using r = new Res();
                      Uses.touch(r);
                    }
                """
            );

        await Assert
            .That(output)
            .Contains(
                """
                    for (let i = 0; i < 2; i++) {
                      using q = new Res();
                """
            );

        await Assert
            .That(output)
            .Contains(
                """
                    const [, v] = d.tryGetValue("a", 0);
                    using r = new Res();
                    Uses.touch(r);
                """
            );

        await Assert
            .That(output)
            .Contains(
                """
                    return () => {
                      using r = new Res();

                      return r.count;
                    };
                """
            );
    }

    [Test]
    public async Task UsingDeclaration_UnderTheTryFinallyOptOut_WrapsTheRestOfItsScope()
    {
        var (files, _) = TranspileHelper.TranspileWithTryFinallyUsings(Resources + NestedScopes);
        var output = files["uses.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    if (f) {
                      const r = new Res();
                      try {
                        Uses.touch(r);
                      } finally {
                        r.dispose();
                      }
                    }
                """
            );

        await Assert
            .That(output)
            .Contains(
                """
                      const q = new Res();
                      try {
                        if (i === 0) {
                          continue;
                        }

                        break;
                      } finally {
                        q.dispose();
                      }
                """
            );

        await Assert
            .That(output)
            .Contains(
                """
                    const [, v] = d.tryGetValue("a", 0);
                    const r = new Res();
                    try {
                      Uses.touch(r);

                      return (v + 1) | 0;
                    } finally {
                      r.dispose();
                    }
                """
            );

        await Assert
            .That(output)
            .Contains(
                """
                    return () => {
                      const r = new Res();
                      try {
                        return r.count;
                      } finally {
                        r.dispose();
                      }
                    };
                """
            );
    }

    [Test]
    public async Task ExplicitForwarder_ToAnInheritedInternalClaimingMember_IsPublishedAsOwnMember()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class OwnerNode
            {
                public int N;
                internal void Dispose() { N++; }
            }

            [Transpile]
            public class EffectNode : OwnerNode, IDisposable
            {
                void IDisposable.Dispose() => Dispose();
            }

            [Transpile]
            public static class Probe
            {
                public static bool Is(object o) => o is IDisposable;
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ExplicitImplementationConflict))
            .IsEmpty();

        await TranspileHelper.AssertGolden("disposable-forwarder.ts", files["effect-node.ts"]);
        await Assert.That(files["owner-node.ts"]).Contains("interfaces: [],");
    }

    [Test]
    public async Task ExplicitDispose_WithItsOwnBody_BesideAClaimingMember_ReportsMS0031()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Both : IDisposable
            {
                public int A;
                public int B;
                public void Dispose() { A++; }
                void IDisposable.Dispose() { B++; }
            }
            """
        );

        var error = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.ExplicitImplementationConflict
        );

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(error.Message).Contains("'Both.Dispose' already claims the name");
    }

    [Test]
    public async Task ExplicitForwarder_ToAProtectedClaimingMember_ReportsMS0031()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class ProtBase
            {
                protected void Dispose() { }
            }

            [Transpile]
            public class ProtDerived : ProtBase, IDisposable
            {
                void IDisposable.Dispose() => Dispose();
            }
            """
        );

        var error = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.ExplicitImplementationConflict
        );

        await Assert.That(error.Message).Contains("'ProtBase.Dispose' already claims the name");
    }

    [Test]
    public async Task AwaitUsing_ReportsMS0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Threading.Tasks;

            [Transpile]
            public sealed class AsyncRes : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }

            [Transpile]
            public static class Uses
            {
                public static async Task Run()
                {
                    await using var r = new AsyncRes();
                    await using (new AsyncRes()) { }
                }
            }
            """
        );

        var errors = diagnostics
            .Where(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature && d.Message.Contains("await using")
            )
            .ToList();

        await Assert.That(errors).Count().IsEqualTo(2);
        await Assert.That(errors.All(e => e.Severity == MetanoDiagnosticSeverity.Error)).IsTrue();
    }

    [Test]
    public async Task UsingDeclaration_InAModuleEntryPoint_ReportsMS0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public sealed class Res : IDisposable
            {
                public void Dispose() { }
            }

            [Transpile]
            public static class Program
            {
                [ModuleEntryPoint]
                public static void Main()
                {
                    using var r = new Res();
                }
            }
            """
        );

        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert.That(error.Message).Contains("module entry point");
        await Assert.That(error.Message).Contains("module's top-level code");
    }

    [Test]
    public async Task UsingStatement_OverAnUnemittedDispose_ReportsMS0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile]
            public static class Uses
            {
                public static void Run(IEnumerator<int> e) { using (e) { } }
            }
            """
        );

        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert.That(error.Message).Contains("IEnumerator<int>");
    }

    [Test]
    public async Task UsingOverADisposeOverloadAddedBelowTheImplementation_ReportsMS0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Conn : IDisposable
            {
                public void Dispose() { }
            }

            [Transpile]
            public class PooledConn : Conn
            {
                public void Dispose(bool returnToPool) { }
            }

            [Transpile]
            public class Door : IDisposable
            {
                public void Dispose() { }
                public void Dispose(bool hard) { }
            }

            [Transpile]
            public class Multi : IDisposable
            {
                public void Dispose() { }
                public void Dispose(int count) { }
            }

            [Transpile]
            public class PooledMulti : Multi
            {
                public void Dispose(bool returnToPool) { }
            }

            [Transpile]
            public static class Uses
            {
                public static void Run()
                {
                    using (var pooled = new PooledConn()) { }
                    using (var door = new Door()) { }
                    using (var multi = new PooledMulti()) { }
                }
            }
            """
        );

        var errors = diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("'PooledConn.Dispose(bool)'");
    }

    [Test]
    public async Task Implementer_WhoseShapeCannotSatisfyTheInterface_ReportsMS0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, PlainObject]
            public record Plain(int X) : IDisposable
            {
                public void Dispose() { }
            }

            [Transpile]
            public class Ignored : IDisposable
            {
                [Ignore]
                public void Dispose() { }
            }
            """
        );

        var messages = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(messages.Any(m => m.Contains("'Plain' implements IDisposable"))).IsTrue();

        await Assert
            .That(messages.Any(m => m.Contains("'Ignored' implements IDisposable")))
            .IsTrue();
    }

    [Test]
    public async Task UsingDeclaration_InAnAsyncMethod_StaysADeclarationBeforeTheAwait()
    {
        var result = TranspileHelper.Transpile(Resources + AsyncUsing);

        await Assert
            .That(result["uses.ts"])
            .Contains(
                """
                    using r = new Res();

                    return await pending;
                """
            );
    }

    [Test]
    public async Task UsingDeclaration_InAnAsyncMethod_UnderTheTryFinallyOptOut_KeepsTheAwaitInsideTheTry()
    {
        var (files, _) = TranspileHelper.TranspileWithTryFinallyUsings(Resources + AsyncUsing);

        await Assert
            .That(files["uses.ts"])
            .Contains(
                """
                    const r = new Res();
                    try {
                      return await pending;
                    } finally {
                      r.dispose();
                    }
                """
            );
    }

    [Test]
    public async Task UserMappedBclInterface_KeepsThe320Shape()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: ExportFromBcl(typeof(IComparable), ExportedName = "IComparable", FromPackage = "my-cmp")]

            [Transpile]
            public class Ordered : IComparable
            {
                public int CompareTo(object? other) => 0;
            }

            [Transpile]
            public static class Probe
            {
                public static bool Is(object o) => o is IComparable;
            }
            """
        );

        var all = string.Concat(files.Values);

        await Assert.That(all).DoesNotContain("IComparable$runtimeType");
        await Assert.That(files["ordered.ts"]).DoesNotContain("implements IComparable");

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsTrue();
    }

    [Test]
    public async Task DisposeGuard_UnderTheTryFinallyOptOut_IsDroppedOnlyForACreatedValue()
    {
        var (files, _) = TranspileHelper.TranspileWithTryFinallyUsings(
            Resources
                + """
                [Transpile]
                public static class Uses
                {
                    public static Res Open() => new Res();

                    public static void Run(Res[] items)
                    {
                        using var created = new Res();
                        using var element = items[0];
                        using var forgiven = Open()!;
                        using var empty = new SRes?();
                    }
                }
                """
        );

        var output = files["uses.ts"];

        await Assert.That(output).Contains("created.dispose();");
        await Assert.That(output).Contains("element?.dispose();");
        await Assert.That(output).Contains("forgiven?.dispose();");
        await Assert.That(output).Contains("empty?.dispose();");
    }

    [Test]
    public async Task DisposeGuard_UnderTheTryFinallyOptOut_IsKeptForACreationThroughAUserDefinedConversion()
    {
        var (files, _) = TranspileHelper.TranspileWithTryFinallyUsings(
            Resources
                + """
                [Transpile]
                public sealed class Wrapper
                {
                    public static implicit operator Res?(Wrapper w) => null;
                }

                [Transpile]
                public static class Uses
                {
                    public static void Run()
                    {
                        using Res? declared = new Wrapper();
                        using (Res? statement = (new Wrapper())) { }
                    }
                }
                """
        );

        var output = files["uses.ts"];

        await Assert.That(output).Contains("declared?.dispose();");
        await Assert.That(output).Contains("statement?.dispose();");
    }

    [Test]
    public async Task CrossPackageImplementer_IsTestedAndDisposed()
    {
        var files = TranspileHelper.TranspileWithLibrary(
            """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace Lib;

            public sealed class LibRes : IDisposable
            {
                public void Dispose() { }
            }
            """,
            """
            using Lib;

            [Transpile]
            public static class Uses
            {
                public static bool Is(object o) => o is IDisposable;
                public static void Run() { using var r = new LibRes(); }
            }
            """
        );

        var output = files["uses.ts"];

        await Assert.That(output).Contains("return isType(o, IDisposable$runtimeType);");
        // A class of a referenced package may have no [Symbol.dispose]() alias.
        await Assert.That(output).Contains("r.dispose();");
        await Assert.That(output).DoesNotContain("using r");
    }

    [Test]
    public async Task Dart_KeepsUsingUnsupportedAndDropsTheInterface()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            """
            using System;
            using Metano.Annotations;

            [Transpile]
            public sealed class Res : IDisposable
            {
                public void Dispose() { }
            }

            [Transpile]
            public static class Uses
            {
                public static void Run() { using (var r = new Res()) { } }
            }
            """
        );

        await Assert.That(string.Concat(files.Values)).DoesNotContain("IDisposable");
        await Assert.That(string.Concat(files.Values)).DoesNotContain("finally");
        await Assert.That(diagnostics.Any(d => d.Message.Contains("using statement"))).IsTrue();
    }
}
