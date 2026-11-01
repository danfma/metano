using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

public class ParamsParameterTests
{
    [Test]
    public async Task InstanceMethod_ParamsObjectArray_EmitsRestParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Logger
            {
                public void Log(string format, params object[] args) { }
            }
            """
        );

        var output = result["app/logger.ts"];
        await Assert.That(output).Contains("log(format: string, ...args: Object[])");
    }

    [Test]
    public async Task InstanceMethod_ParamsGenericArray_EmitsRestParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Bag<T>
            {
                public void AddRange(params T[] items) { }
            }
            """
        );

        var output = result["app/bag.ts"];
        await Assert.That(output).Contains("addRange(...items: T[])");
    }

    [Test]
    public async Task CallSite_DiscreteArgs_PassThroughWithoutArrayWrap()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Logger
            {
                public void Log(string format, params object[] args) { }

                public void Demo()
                {
                    Log("hi {0} {1}", "alice", 42);
                }
            }
            """
        );

        var output = result["app/logger.ts"];
        await Assert.That(output).Contains("this.log(\"hi {0} {1}\", \"alice\", 42)");
        await Assert.That(output).DoesNotContain("[\"alice\", 42]");
    }

    [Test]
    public async Task ModuleFunction_Params_EmitsRestParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [NoContainer]
            public static class Logger
            {
                public static void Log(string format, params object[] args) { }
            }
            """
        );

        var output = result["app/logger.ts"];
        await Assert.That(output).Contains("function log(format: string, ...args: Object[])");
    }

    [Test]
    public async Task Constructor_Params_EmitsRestParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Tagged
            {
                public Tagged(string label, params string[] tags) { }
            }
            """
        );

        var output = result["app/tagged.ts"];
        await Assert.That(output).Contains("constructor(label: string, ...tags: string[])");
    }

    [Test]
    public async Task Interface_Params_EmitsRestParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public interface IPrinter
            {
                void Write(string format, params object[] args);
            }
            """
        );

        var output = result["app/i-printer.ts"];
        await Assert.That(output).Contains("write(format: string, ...args: Object[])");
    }

    [Test]
    public async Task Record_PositionalParams_SplitsFieldAndRestCtorParam()
    {
        // #152: TS forbids combining a parameter-property modifier with the
        // rest prefix (`readonly ...tags: string[]` is a syntax error).
        // The bridge splits the slot: declare a separate `readonly tags`
        // field, emit the ctor with `...tags: string[]`, and copy across in
        // the body. This restores the variadic call surface that #145 had
        // to suppress.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed record Tagged(string Label, params string[] Tags);
            """
        );

        var output = result["app/tagged.ts"];
        await Assert.That(output).Contains("readonly tags: string[]");
        await Assert.That(output).Contains("...tags: string[]");
        await Assert.That(output).Contains("this.tags = tags;");
        await Assert.That(output).DoesNotContain("readonly ...tags");
    }

    [Test]
    public async Task Record_PositionalParams_VariadicCallSiteSpreadsArray()
    {
        // With the field/rest split in place, the call-site spread that #145
        // had to suppress for record ctors is back: passing an array to the
        // params slot lowers as `new Tagged("hi", ...tags)`.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed record Tagged(string Label, params string[] Tags);

            [Transpile]
            public sealed class Caller
            {
                public Tagged Build(string[] tags) => new Tagged("hi", tags);
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("new Tagged(\"hi\", ...tags)");
    }

    [Test]
    public async Task Record_PositionalParams_DiscreteArgsPassThrough()
    {
        // Regression guard: discrete-arg call sites stay unchanged — the
        // emitted ctor's rest-parameter swallows them like any other `params`.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed record Tagged(string Label, params string[] Tags);

            [Transpile]
            public sealed class Caller
            {
                public Tagged Build() => new Tagged("hi", "a", "b", "c");
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("new Tagged(\"hi\", \"a\", \"b\", \"c\")");
    }

    [Test]
    public async Task CallSite_ExplicitArrayArgument_IsSpread()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Logger
            {
                public void Log(string format, params string[] args) { }

                public void Demo(string[] tags)
                {
                    Log("hi", tags);
                }
            }
            """
        );

        var output = result["app/logger.ts"];
        await Assert.That(output).Contains("this.log(\"hi\", ...tags)");
    }

    [Test]
    public async Task NewExpression_ExplicitArrayArgument_IsSpread()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Tagged
            {
                public Tagged(string label, params string[] tags) { }
            }

            [Transpile]
            public sealed class Caller
            {
                public Tagged Make(string[] tags) => new Tagged("hi", tags);
            }
            """
        );

        var output = result["app/caller.ts"];
        await Assert.That(output).Contains("new Tagged(\"hi\", ...tags)");
    }

    [Test]
    public async Task CustomDelegate_Params_EmitsRestParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            public delegate void LoggerFn(string format, params object[] args);

            [Transpile]
            public sealed class Host
            {
                public LoggerFn? Logger { get; set; }
            }
            """
        );

        var output = result["app/host.ts"];
        await Assert.That(output).Contains("(format: string, ...args: Object[]) => void");
    }

    [Test]
    public async Task CallSite_NullParamsArray_DropsTheArgumentInsteadOfSpreadingIt()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public sealed class Logger
            {
                public int Count(string label, params string[] items) => items.Length;

                public int Demo() => Count("none", null!) + Count("default", default!);
            }
            """
        );

        var output = result["app/logger.ts"];
        await Assert.That(output).Contains("this.count(\"none\") + this.count(\"default\")");
        await Assert.That(output).DoesNotContain("...null");
        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Severity == MetanoDiagnosticSeverity.Warning
                    && d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("params parameter")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task NewExpression_NullParamsArray_DropsTheArgument()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Tagged
            {
                public Tagged(string label, params string[] tags) { }
            }

            [Transpile]
            public sealed class Caller
            {
                public Tagged Make() => new Tagged("hi", null!);
            }
            """
        );

        await Assert.That(result["app/caller.ts"]).Contains("new Tagged(\"hi\")");
    }

    [Test]
    public async Task CallSite_NullCastToElementType_StaysADiscreteElement()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Logger
            {
                public int Count(params string?[] items) => items.Length;

                public int Demo() => Count((string?)null);
            }
            """
        );

        await Assert.That(result["app/logger.ts"]).Contains("this.count(null)");
    }

    [Test]
    public async Task CallSite_NullCastToParamsArray_DropsTheArgumentAndReportsItsLocation()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public sealed class Logger
            {
                public int Count(params string[]? items) => items?.Length ?? -1;

                public int Demo() => Count((string[]?)null) + Count(default(string[]));
            }
            """
        );

        var output = result["app/logger.ts"];
        await Assert.That(output).Contains("this.count() + this.count()");
        await Assert.That(output).DoesNotContain("...null");

        var warnings = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();
        await Assert.That(warnings.All(d => d.Location is not null)).IsTrue();
        await Assert
            .That(warnings.Select(d => d.Location!.SourceSpan).Distinct().Count())
            .IsEqualTo(2);
    }

    [Test]
    public async Task NullableParamsArray_EmitsANonNullableRestParameterAtEveryDeclarationSite()
    {
        // A rest parameter must have an array type (TS2370) and never holds null, so
        // a `params T[]?` drops its nullability on every lowering that emits `...`:
        // methods, module functions, interfaces, delegates, constructors, positional
        // records and [PlainObject] helpers. The promoted record field keeps the
        // declared nullability.
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile, EmitInFile("nullable-params")]
            public static class Calc
            {
                public static int Sum(params int[]? xs) => xs?.Length ?? -1;

                public static string Join(string label, params string?[]? parts) =>
                    label + (parts?.Length ?? -1);
            }

            [Transpile, NoContainer, EmitInFile("nullable-params")]
            public static class Fns
            {
                public static int Count<T>(params T[]? items) => items?.Length ?? -1;
            }

            [Transpile, EmitInFile("nullable-params")]
            public interface ISummer
            {
                int SumAll(params int[]? xs);
            }

            [Transpile, EmitInFile("nullable-params")]
            public delegate int Folder(params int[]? xs);

            [Transpile, EmitInFile("nullable-params")]
            public record Bag(params int[]? Items);

            [Transpile, EmitInFile("nullable-params")]
            public class Box
            {
                public int N { get; }

                public Box(params int[]? xs)
                {
                    N = xs?.Length ?? -1;
                }
            }

            [Transpile, PlainObject, EmitInFile("nullable-params")]
            public record Point(int X)
            {
                public int Offset(params int[]? deltas) => X + (deltas?.Length ?? 0);
            }
            """
        );

        await Assert
            .That(result["app/nullable-params.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("nullable-params-rest.ts"));
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task NullableParamsArray_OverloadSignaturesEmitNonNullableRestParameters()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public static class Calc
            {
                public static int Pick(int x) => x;

                public static int Pick(string label, params int[]? xs) => xs?.Length ?? -1;
            }
            """
        );

        var output = result["app/calc.ts"];
        await Assert.That(output).Contains("static pick(label: string, ...xs: number[]): number;");
        await Assert
            .That(output)
            .Contains("private static pickLabelXs(label: string, ...xs: number[]): number {");
        await Assert.That(output).DoesNotContain("...xs: number[] | null");
    }

    [Test]
    public async Task NullableParamsArray_NullCallSiteIsStillReported()
    {
        // The callee's rest parameter drops `| null`; a call that passes a null array
        // keeps the #320 handling: the argument is dropped and MS0001 is reported.
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public sealed class Logger
            {
                public int Count(params string[]? items) => items?.Length ?? -1;

                public int Demo() => Count(null);
            }
            """
        );

        var output = result["app/logger.ts"];
        await Assert.That(output).Contains("count(...items: string[]): number {");
        await Assert.That(output).Contains("return this.count();");
        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Severity == MetanoDiagnosticSeverity.Warning
                    && d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("params parameter")
                )
            )
            .IsTrue();
    }
}
