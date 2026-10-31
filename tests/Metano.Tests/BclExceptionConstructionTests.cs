namespace Metano.Tests;

/// <summary>
/// BCL exception constructions (<c>new ArgumentException(...)</c>, the
/// <c>: base(...)</c> call of a user exception deriving from a BCL exception)
/// lower to the JS <c>Error(message, { cause })</c> shape with every argument
/// mapped by its constructor-parameter role, emulating how .NET composes
/// <c>Exception.Message</c> (#246).
/// </summary>
public class BclExceptionConstructionTests
{
    [Test]
    public async Task ArgumentException_AppendsParamNameToMessage()
    {
        var output = TranspileGuard(
            "public static void Check(string value) { throw new ArgumentException(\"msg\", nameof(value)); }"
        );

        await Assert.That(output).Contains("throw new Error(\"msg (Parameter 'value')\");");
    }

    [Test]
    public async Task ArgumentNullException_WithParamNameOnly_UsesDotNetDefaultMessage()
    {
        var output = TranspileGuard(
            "public static void Check(string value) { throw new ArgumentNullException(nameof(value)); }"
        );

        await Assert
            .That(output)
            .Contains("throw new Error(\"Value cannot be null. (Parameter 'value')\");");
    }

    [Test]
    public async Task ArgumentNullException_WithParamNameAndMessage_KeepsMessageFirst()
    {
        var output = TranspileGuard(
            "public static void Check(string value) { throw new ArgumentNullException(nameof(value), \"custom\"); }"
        );

        await Assert.That(output).Contains("throw new Error(\"custom (Parameter 'value')\");");
    }

    [Test]
    public async Task ArgumentOutOfRangeException_AppendsParamNameAndActualValue()
    {
        var output = TranspileGuard(
            "public static void Check(int value) { throw new ArgumentOutOfRangeException(nameof(value), value, \"too big\"); }"
        );

        await Assert
            .That(output)
            .Contains(
                "throw new Error(`too big (Parameter 'value')\\nActual value was ${value}.`);"
            );
    }

    [Test]
    public async Task NamedArgumentsOutOfOrder_MapByParameterRole()
    {
        var output = TranspileGuard(
            "public static void Check(string value) { throw new ArgumentException(paramName: nameof(value), message: \"named\"); }"
        );

        await Assert.That(output).Contains("throw new Error(\"named (Parameter 'value')\");");
    }

    [Test]
    public async Task InnerException_LowersToErrorCause()
    {
        var output = TranspileGuard(
            "public static void Wrap(Exception inner) { throw new Exception(\"outer\", inner); }"
        );

        await Assert.That(output).Contains("throw new Error(\"outer\", { cause: inner });");
    }

    [Test]
    public async Task InvalidOperationException_KeepsMessage()
    {
        var output = TranspileGuard(
            "public static void Fail() { throw new InvalidOperationException(\"nope\"); }"
        );

        await Assert.That(output).Contains("throw new Error(\"nope\");");
    }

    [Test]
    public async Task NonLiteralMessageAndParamName_ComposeTemplateLiteral()
    {
        var output = TranspileGuard(
            "public static void Check(string message, string name) { throw new ArgumentException(message, name); }"
        );

        await Assert.That(output).Contains("throw new Error(`${message} (Parameter '${name}')`);");
    }

    [Test]
    public async Task ExceptionTypedParameterAndLocal_MapToError()
    {
        var output = TranspileGuard(
            "public static Exception Rethrow(Exception error) { Exception local = error; return local; }"
        );

        await Assert.That(output).Contains("static rethrow(error: Error): Error {");
        await Assert.That(output).Contains("const local = error;");
        await Assert.That(output).DoesNotContain(": Exception");
    }

    [Test]
    public async Task TypedCatch_KeepsUntypedCatchClause()
    {
        var output = TranspileGuard(
            """
            public static string Read()
            {
                try
                {
                    Fail();
                    return "ok";
                }
                catch (ArgumentException e)
                {
                    return e.Message;
                }
            }

            public static void Fail() { throw new ArgumentException("bad"); }
            """
        );

        await Assert.That(output).Contains("} catch (e) {");

        await Assert.That(output).Contains("return e.message;");
    }

    [Test]
    public async Task UserExceptionDerivingFromArgumentException_MapsBaseArgumentsByRole()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public class InvalidSlugException : ArgumentException
            {
                public InvalidSlugException(string slug)
                    : base("Invalid slug", slug) { }
            }
            """
        );

        await Assert
            .That(result["invalid-slug-exception.ts"])
            .Contains("super(`Invalid slug (Parameter '${slug}')`);");
    }

    [Test]
    public async Task UserExceptionWithInnerException_ForwardsCauseToSuper()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public sealed class LoadFailedException(string message, Exception inner)
                : Exception(message, inner);
            """
        );

        var output = result["load-failed-exception.ts"];

        await Assert.That(output).Contains("constructor(message: string, inner: Error)");
        await Assert.That(output).Contains("super(message, { cause: inner });");
    }

    [Test]
    public async Task NullMessageLiteral_UsesDefaultMessage()
    {
        var output = TranspileGuard(
            "public static void Check(string value) { throw new ArgumentException(null, nameof(value)); }"
        );

        await Assert
            .That(output)
            .Contains(
                "throw new Error(\"Value does not fall within the expected range. (Parameter 'value')\");"
            );
    }

    [Test]
    public async Task EmptyParamNameLiteral_OmitsParameterSuffix()
    {
        var output = TranspileGuard(
            "public static void Check() { throw new ArgumentException(\"msg\", \"\"); }"
        );

        await Assert.That(output).Contains("throw new Error(\"msg\");");
    }

    [Test]
    public async Task UnmappedSideEffectFreeArgument_IsDropped()
    {
        var output = TranspileGuard(
            "public static void Load(string path) { throw new System.IO.FileNotFoundException(\"missing\", path); }"
        );

        await Assert.That(output).Contains("throw new Error(\"missing\");");
    }

    [Test]
    public async Task SideEffectingArguments_EvaluateOnceInSourceOrder()
    {
        var output = TranspileGuard(
            """
            public static void Load() { throw new System.IO.FileNotFoundException(Describe(), Locate()); }

            public static string Describe() => "missing";

            public static string Locate() => "file.txt";
            """
        );

        await Assert
            .That(output)
            .Contains(
                "throw ((receiver$temp$0) => ((receiver$temp$1) => new Error(receiver$temp$0))(Guard.locate()))(Guard.describe());"
            );
    }

    [Test]
    public async Task SideEffectingArgumentsInParamNameFirstOrder_ComposeFromTemporaries()
    {
        var output = TranspileGuard(
            """
            public static void Check() { throw new ArgumentOutOfRangeException(Name(), Describe()); }

            public static string Name() => "value";

            public static string Describe() => "too big";
            """
        );

        await Assert
            .That(output)
            .Contains(
                "throw ((receiver$temp$0) => ((receiver$temp$1) => new Error(`${receiver$temp$1} (Parameter '${receiver$temp$0}')`))(Guard.describe()))(Guard.name());"
            );
    }

    [Test]
    public async Task SingleSideEffectingMappedArgument_StaysInline()
    {
        var output = TranspileGuard(
            """
            public static void Check() { throw new ArgumentOutOfRangeException(Name(), "too big"); }

            public static string Name() => "value";
            """
        );

        await Assert
            .That(output)
            .Contains("throw new Error(`too big (Parameter '${Guard.name()}')`);");
    }

    [Test]
    public async Task SideEffectingUnmappedArgument_IsStillEvaluated()
    {
        var output = TranspileGuard(
            """
            public static void Load() { throw new System.IO.FileNotFoundException("missing", Locate()); }

            public static string Locate() => "file.txt";
            """
        );

        await Assert
            .That(output)
            .Contains("throw ((receiver$temp$0) => new Error(\"missing\"))(Guard.locate());");
    }

    [Test]
    public async Task ObjectDisposedException_AppendsObjectName()
    {
        var output = TranspileGuard(
            "public static void Use() { throw new ObjectDisposedException(\"Connection\"); }"
        );

        await Assert
            .That(output)
            .Contains(
                "throw new Error(`Cannot access a disposed object.\\nObject name: 'Connection'.`);"
            );
    }

    [Test]
    public async Task NullableMessage_FallsBackToDefaultMessageAtRuntime()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            using System;

            [Transpile]
            public sealed class OptionalMessageException(string? message = null, Exception? inner = null)
                : Exception(message, inner);
            """
        );

        await Assert
            .That(result["optional-message-exception.ts"])
            .Contains(
                "super(message ?? \"Exception of type 'OptionalMessageException' was thrown.\", { cause: inner });"
            );
    }

    [Test]
    public async Task GenericUserException_NamesTypeLikeDotNet()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            namespace App
            {
                [Transpile]
                public sealed class LookupFailedException<TKey> : Exception
                {
                    public LookupFailedException(TKey key)
                    {
                        Key = key;
                    }

                    public TKey Key { get; }
                }
            }
            """
        );

        await Assert
            .That(result["app/lookup-failed-exception.ts"])
            .Contains(
                "super(\"Exception of type 'App.LookupFailedException`1[TKey]' was thrown.\");"
            );
    }

    [Test]
    public async Task NewOfTranspilableUserException_KeepsItsOwnClassAndArguments()
    {
        var output = TranspileGuard(
            """
            public static void Check(string value) { throw new SlugException("bad slug", value); }
            }

            [Transpile]
            public sealed class SlugException(string message, string paramName) : Exception(message)
            {
                public string ParamName { get; } = paramName;
            """
        );

        await Assert.That(output).Contains("throw new SlugException(\"bad slug\", value);");
    }

    [Test]
    public async Task ExceptionMemberReads_MapToErrorShape()
    {
        var output = TranspileGuard(
            """
            public static Exception? Inner(Exception error) => error.InnerException;

            public static string? Trace(Exception error) => error.StackTrace;

            public static Exception InnerOr(Exception error, Exception other) => error.InnerException ?? other;

            public static string TraceOrEmpty(Exception error) => error.StackTrace ?? "";
            """
        );

        await Assert
            .That(output)
            .Contains("return (error.cause instanceof Error ? error.cause : null);");

        await Assert
            .That(output)
            .Contains("return (error.stack !== undefined ? error.stack : null);");

        await Assert
            .That(output)
            .Contains("return (error.cause instanceof Error ? error.cause : null) ?? other;");

        await Assert
            .That(output)
            .Contains("return (error.stack !== undefined ? error.stack : null) ?? \"\";");
    }

    [Test]
    public async Task CastNullArguments_CountAsAbsent()
    {
        var output = TranspileGuard(
            """
            public static void Check() { throw new ArgumentNullException((string?)null); }

            public static void Use() { throw new ObjectDisposedException((string?)null); }
            """
        );

        await Assert.That(output).Contains("throw new Error(\"Value cannot be null.\");");

        await Assert
            .That(output)
            .Contains("throw new Error(\"Cannot access a disposed object.\");");
    }

    [Test]
    public async Task ParameterlessIoAndTaskExceptions_UseDotNetDefaultMessages()
    {
        var output = TranspileGuard(
            """
            public static void Missing() { throw new System.IO.FileNotFoundException(); }

            public static void Canceled() { throw new System.Threading.Tasks.TaskCanceledException(); }

            public static void Truncated() { throw new System.IO.EndOfStreamException(); }
            """
        );

        await Assert
            .That(output)
            .Contains("throw new Error(\"Unable to find the specified file.\");");

        await Assert.That(output).Contains("throw new Error(\"A task was canceled.\");");

        await Assert
            .That(output)
            .Contains("throw new Error(\"Attempted to read past the end of the stream.\");");
    }

    [Test]
    public async Task SideEffectingBaseArguments_DeclareTemporariesBeforeSuper()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public sealed class MissingFileException(string name)
                : System.IO.FileNotFoundException(Describe(name), Locate(name))
            {
                private static string Describe(string name) => "missing " + name;

                private static string Locate(string name) => "/" + name;
            }
            """
        );

        var output = result["missing-file-exception.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    const __baseArg0 = MissingFileException.describe(name);
                    const __baseArg1 = MissingFileException.locate(name);
                    super(__baseArg0);
                """
            );
    }

    [Test]
    public async Task AwaitedSideEffectingArguments_BindInsideAsyncIife()
    {
        var output = TranspileGuard(
            """
            public static async System.Threading.Tasks.Task<Exception> Build() =>
                new ArgumentOutOfRangeException(await Name(), await Describe());

            public static async System.Threading.Tasks.Task<string> Name() { await System.Threading.Tasks.Task.CompletedTask; return "value"; }

            public static async System.Threading.Tasks.Task<string> Describe() { await System.Threading.Tasks.Task.CompletedTask; return "too big"; }
            """
        );

        await Assert
            .That(output)
            .Contains(
                "return await (async (receiver$temp$0) => ((receiver$temp$1) => new Error(`${receiver$temp$1} (Parameter '${receiver$temp$0}')`))(await Guard.describe()))(await Guard.name());"
            );
    }

    private static string TranspileGuard(string members)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            using System;

            [Transpile]
            public static class Guard
            {
                {{members}}
            }
            """
        );

        return result["guard.ts"];
    }
}
