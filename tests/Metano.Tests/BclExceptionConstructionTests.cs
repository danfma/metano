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

        await Assert.That(output).Contains("static rethrow(error: Error): Error");
        await Assert.That(output).DoesNotContain("Exception");
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
