namespace MetaSharp.Tests;

public class GenericNameTests
{
    [Test]
    public async Task GenericStaticMethodCall_ResolvesCorrectly()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Result<T>
            {
                public T? Value { get; }
                public bool IsSuccess { get; }

                public Result(T? value, bool isSuccess)
                {
                    Value = value;
                    IsSuccess = isSuccess;
                }

                public static Result<T> Ok(T value) { return new Result<T>(value, true); }
                public static Result<T> Fail() { return new Result<T>(default, false); }
            }

            [Transpile]
            public class Service
            {
                public Result<string> Process(string input)
                {
                    return Result<string>.Ok(input);
                }
            }
            """
        );

        var output = result["service.ts"];
        await Assert.That(output).DoesNotContain("unsupported");
        await Assert.That(output).DoesNotContain("GenericName");
        await Assert.That(output).Contains("Result.ok(input)");
    }
}
