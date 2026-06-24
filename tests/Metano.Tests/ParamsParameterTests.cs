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
}
