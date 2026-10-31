namespace Metano.Tests;

public class ExceptionTranspileTests
{
    [Test]
    public async Task SimpleException_ExtendsError()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class InvalidAmountError(string message) : Exception(message);
            """
        );

        var expected = TranspileHelper.ReadExpected("custom-exception.ts");
        await Assert.That(result["invalid-amount-error.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task PrimaryCtorException_InterpolatesMessage()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class DuplicateEntryException(string name)
                : Exception($"Duplicate entry: {name}");
            """
        );

        var expected = TranspileHelper.ReadExpected("primary-ctor-exception.ts");
        await Assert.That(result["duplicate-entry-exception.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task ThrowTranspiledException_UsesClassName()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public sealed class BadValueError(string message) : Exception(message);

                [Transpile]
                public readonly record struct Validator(int Max)
                {
                    public static void Check(int value)
                    {
                        if (value < 0)
                            throw new BadValueError("negative");
                    }
                }
            }
            """
        );

        var validatorTs = result["app/validator.ts"];
        // Should use the transpiled exception class, not generic Error
        await Assert.That(validatorTs).Contains("new BadValueError(\"negative\")");
        await Assert.That(validatorTs).DoesNotContain("new Error(");
    }

    [Test]
    public async Task ThrowNonTranspiledException_UsesError()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Guard(int X)
            {
                public static void Verify(int x)
                {
                    if (x < 0)
                        throw new System.ArgumentException("bad");
                }
            }
            """
        );

        var output = result["guard.ts"];
        // Non-transpiled exception → Error
        await Assert.That(output).Contains("new Error(\"bad\")");
    }

    [Test]
    public async Task Exception_NoRecordMembers()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class MyError(string message) : Exception(message);
            """
        );

        var output = result["my-error.ts"];
        // Exceptions should not have equals/hashCode/with
        await Assert.That(output).DoesNotContain("equals");
        await Assert.That(output).DoesNotContain("hashCode");
        await Assert.That(output).DoesNotContain("with(");
    }

    [Test]
    public async Task ExceptionWithMembers_KeepsPropertiesMethodsAndConstructorBody()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class QuotaExceededException : Exception
            {
                public QuotaExceededException(string message, int limit)
                    : base(message)
                {
                    Limit = limit;
                }

                public int Limit { get; }

                public string Describe() => $"limit {Limit}: {Message}";
            }
            """
        );

        var output = result["quota-exceeded-exception.ts"];
        await Assert.That(output).Contains("export class QuotaExceededException extends Error {");
        await Assert
            .That(output)
            .Contains("constructor(message: string, readonly limit: number) {");
        await Assert.That(output).Contains("super(message);");
        await Assert.That(output).Contains("describe(): string {");
        await Assert.That(output).Contains("`limit ${this.limit}: ${this.message}`");
    }

    [Test]
    public async Task ExceptionWithoutBaseInitializer_UsesDotNetDefaultMessage()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public sealed class StaleStateException : InvalidOperationException
                {
                    public StaleStateException(int version)
                    {
                        Version = version;
                    }

                    public int Version { get; }
                }
            }
            """
        );

        var output = result["app/stale-state-exception.ts"];
        await Assert
            .That(output)
            .Contains("super(\"Operation is not valid due to the current state of the object.\");");
        await Assert.That(output).Contains("this.version = version;");
    }

    [Test]
    public async Task ExceptionWithConstructorOverloads_DispatchesSuperByRole()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public class SyncException : Exception
                {
                    public SyncException() { }

                    public SyncException(string message)
                        : base(message) { }

                    public SyncException(string message, Exception inner)
                        : base(message, inner) { }
                }
            }
            """
        );

        var output = result["app/sync-exception.ts"];
        await Assert.That(output).Contains("export class SyncException extends Error {");
        await Assert.That(output).Contains("super(message, { cause: inner });");
        await Assert.That(output).Contains("super(message);");
        await Assert
            .That(output)
            .Contains("super(\"Exception of type 'App.SyncException' was thrown.\");");
    }
}
