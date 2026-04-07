namespace MetaSharp.Tests;

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

        var validatorTs = result["validator.ts"];
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
}
