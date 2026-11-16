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

        await TranspileHelper.AssertGolden(
            "custom-exception.ts",
            result["invalid-amount-error.ts"]
        );
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

        await TranspileHelper.AssertGolden(
            "primary-ctor-exception.ts",
            result["duplicate-entry-exception.ts"]
        );
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
    public async Task ThrowNonTranspiledException_UsesTheRuntimeClass()
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
        // Non-transpiled exception → its metano-runtime class (spec 011 FR-014)
        await Assert.That(output).Contains("new ArgumentException(\"bad\")");
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
            #nullable enable
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

        await Assert
            .That(output)
            .Contains("export class QuotaExceededException extends Exception {");

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

        // The runtime class applies .NET's default message itself.
        await Assert
            .That(output)
            .Contains("export class StaleStateException extends InvalidOperationException {");

        await Assert.That(output).Contains("super();");
        await Assert.That(output).DoesNotContain("Operation is not valid");
        await Assert.That(output).Contains("this.version = version;");
    }

    [Test]
    public async Task ExceptionWithConstructorOverloads_DispatchesSuperByRole()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
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

        await Assert.That(output).Contains("export class SyncException extends Exception {");
        await Assert.That(output).Contains("super(message, inner);");
        await Assert.That(output).Contains("super(message);");

        await Assert
            .That(output)
            .Contains("super(\"Exception of type 'App.SyncException' was thrown.\");");
    }

    [Test]
    public async Task ExceptionWithTwoLevelThisChain_InlinesChainIntoSingleSuperCall()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            namespace App
            {
                [Transpile]
                public sealed class ChainException : Exception
                {
                    public ChainException()
                        : this("first") { }

                    public ChainException(string message)
                        : this(message, 7) { }

                    public ChainException(string message, int code)
                        : base(message)
                    {
                        Code = code;
                    }

                    public int Code { get; }
                }
            }
            """
        );

        var output = result["app/chain-exception.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                    if (args.length === 0) {
                      const __chain0Arg0 = "first";

                      {
                        const message = __chain0Arg0;
                        const __chain1Arg0 = message;
                        const __chain1Arg1 = 7;

                        {
                          const message = __chain1Arg0;
                          const code = __chain1Arg1;
                          super(message);
                          this.code = code;
                        }
                      }

                      return;
                    }
                """
            );
    }

    [Test]
    public async Task ExceptionOverridingMessage_MovesNativeMessageIntoKeptField()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class RetryException(int attempts, Exception inner)
                : Exception("ignored", inner)
            {
                public override string Message => $"Failed after {attempts} attempts";
            }
            """
        );

        var output = result["retry-exception.ts"];

        await Assert.That(output).Contains("readonly #baseMessage: string;");
        await Assert.That(output).Contains("super(\"ignored\", inner);");

        await Assert
            .That(output)
            .Contains(
                "this.#baseMessage = Object.hasOwn(this, \"message\") ? this.message : super.message;"
            );
        await Assert.That(output).Contains("Reflect.deleteProperty(this, \"message\");");
        await Assert.That(output).Contains("get message(): string {");
    }

    [Test]
    public async Task ExceptionWithoutConstructors_UsesDotNetDefaultMessage()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class NoConstructorException : Exception { }
            """
        );

        await Assert
            .That(result["no-constructor-exception.ts"])
            .Contains("super(\"Exception of type 'NoConstructorException' was thrown.\");");
    }

    [Test]
    public async Task ExceptionOverridingMessageWithoutExplicitConstructor_DeclaresKeptField()
    {
        // Implicit and primary constructors must still declare and assign the kept
        // base-message field that `base.Message` reads.
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            using System;

            [Transpile]
            public class NoCtorException : Exception
            {
                public override string Message => "custom: " + base.Message;
            }

            [Transpile]
            public class PrimaryException(string detail) : Exception(detail)
            {
                public override string Message => "p: " + base.Message;
            }
            """
        );

        var implicitCtor = result["no-ctor-exception.ts"];
        var primaryCtor = result["primary-exception.ts"];

        await Assert.That(implicitCtor).Contains("readonly #baseMessage: string;");

        await Assert
            .That(implicitCtor)
            .Contains("super(\"Exception of type 'NoCtorException' was thrown.\");");

        await Assert.That(primaryCtor).Contains("readonly #baseMessage: string;");
        await Assert.That(primaryCtor).Contains("super(detail);");
    }

    [Test]
    public async Task ExceptionOverridingMessage_ReadsBaseMessageFromKeptField()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile]
            public sealed class DetailedException(string detail) : Exception(detail)
            {
                public override string Message => "custom: " + base.Message;
            }
            """
        );

        var output = result["detailed-exception.ts"];

        await Assert.That(output).Contains("super(detail);");
        await Assert
            .That(output)
            .Contains(
                "this.#baseMessage = Object.hasOwn(this, \"message\") ? this.message : super.message;"
            );
        await Assert.That(output).Contains("return \"custom: \" + this.#baseMessage;");
    }

    [Test]
    public async Task IgnoredMessageOverride_KeepsNativeMessage()
    {
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile]
            public sealed class QuietException(string detail) : Exception(detail)
            {
                [Ignore]
                public override string Message => "dotnet only: " + base.Message;
            }
            """
        );

        var output = result["quiet-exception.ts"];

        await Assert.That(output).Contains("super(detail);");
        await Assert.That(output).DoesNotContain("#baseMessage");
        await Assert.That(output).DoesNotContain("get message()");
    }

    [Test]
    public async Task MessageOverridesAcrossDispatcherChainsAndAncestors_MatchGolden()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public class OverloadOverrideException : Exception
                {
                    public OverloadOverrideException() { }

                    public OverloadOverrideException(string message)
                        : base(message) { }

                    public OverloadOverrideException(string message, Exception inner)
                        : this(message)
                    {
                        Origin = inner;
                    }

                    public Exception? Origin { get; }

                    public override string Message => "wrapped: " + base.Message;
                }

                [Transpile]
                public class InvalidSlotException : ArgumentException
                {
                    public InvalidSlotException(string message, string slot)
                        : base(message, slot) { }
                }

                [Transpile]
                public sealed class LabelledSlotException : InvalidSlotException
                {
                    public LabelledSlotException()
                        : base("bad slot", "slot") { }

                    public override string Message => "labelled: " + base.Message;
                }
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "exception-overload-message-override.ts",
            result["app/overload-override-exception.ts"]
        );

        await TranspileHelper.AssertGolden(
            "exception-ancestor-message-override.ts",
            result["app/labelled-slot-exception.ts"]
        );
    }
}
