using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>System.IDisposable</c> and <c>using</c> (FR-111, #800): the runtime interface
/// <c>IDisposable { dispose(): void }</c> kept in heritage clauses and descriptors and
/// tested nominally, <c>using</c> lowered to <c>try</c>/<c>finally</c>, and the explicit
/// <c>IDisposable.Dispose()</c> forwarder rules.
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

    [Test]
    public async Task Implementer_KeepsTheInterfaceInImplementsAndItsDescriptor()
    {
        var result = TranspileHelper.Transpile(
            Resources
                + """
                [Transpile]
                public static class Probe
                {
                    public static bool Is(object o) => o is IDisposable;
                }
                """
        );

        await TranspileHelper.AssertGolden("disposable-implementer.ts", result["res.ts"]);
        await TranspileHelper.AssertGolden("disposable-explicit.ts", result["handle.ts"]);
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
    public async Task UsingForms_LowerToTryFinally()
    {
        var result = TranspileHelper.Transpile(
            Resources
                + """
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
                """
        );

        await TranspileHelper.AssertGolden("disposable-using-forms.ts", result["uses.ts"]);
    }

    [Test]
    public async Task UsingDeclaration_InNestedScopesAndLambdas_WrapsTheRestOfItsScope()
    {
        var result = TranspileHelper.Transpile(
            Resources
                + """
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
                """
        );

        var output = result["uses.ts"];

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
    public async Task ExplicitForwarder_ToTheClaimingMember_IsDroppedSilently()
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
    public async Task UsingDeclaration_InAnAsyncMethod_KeepsTheAwaitInsideTheTry()
    {
        var result = TranspileHelper.Transpile(
            Resources
                + """
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
                """
        );

        await Assert
            .That(result["uses.ts"])
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
    public async Task DisposeGuard_IsDroppedOnlyForACreatedValue()
    {
        var result = TranspileHelper.Transpile(
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
                    }
                }
                """
        );

        var output = result["uses.ts"];

        await Assert.That(output).Contains("created.dispose();");
        await Assert.That(output).Contains("element?.dispose();");
        await Assert.That(output).Contains("forgiven?.dispose();");
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
        // A created value is never null, so it needs no guard.
        await Assert.That(output).Contains("r.dispose();");
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
