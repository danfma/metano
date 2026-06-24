namespace Metano.Tests;

/// <summary>
/// Default-value emission for instance / static method parameters and
/// module-function parameters. The IR captures the default expression
/// on these sites; these tests pin the TypeScript surface so callers
/// see the same optional ergonomics the C# source intends. Lambda
/// parameters are out of scope — C# disallows defaults on lambdas;
/// declaration-only positions (abstract / overload / interface
/// signatures) drop the initializer because TypeScript forbids them
/// there but keep the parameter optional via the <c>?</c> suffix.
/// </summary>
public class DefaultParameterValueTests
{
    [Test]
    public async Task InstanceMethod_StringDefault_EmitsAssignment()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Greeter
            {
                public string Greet(string name, string greeting = "hello") =>
                    $"{greeting}, {name}";
            }
            """
        );

        var output = result["app/greeter.ts"];
        await Assert.That(output).Contains("greet(name: string, greeting: string = \"hello\")");
    }

    [Test]
    public async Task ModuleFunction_BoolDefault_EmitsAssignment()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            [NoContainer]
            public static class Builder
            {
                public static string Create(string tag, bool attached = true) =>
                    attached ? tag : $"<{tag}>";
            }
            """
        );

        var output = result["app/builder.ts"];
        await Assert
            .That(output)
            .Contains("function create(tag: string, attached: boolean = true): string");
    }

    [Test]
    public async Task NumericDefault_EmitsLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Counter
            {
                public int Tick(int step = 1) => step;
            }
            """
        );

        var output = result["app/counter.ts"];
        await Assert.That(output).Contains("tick(step: number = 1)");
    }

    [Test]
    public async Task NullDefault_EmitsNullLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Logger
            {
                public void Log(string message, string? prefix = null) { }
            }
            """
        );

        var output = result["app/logger.ts"];
        await Assert.That(output).Contains("log(message: string, prefix: string | null = null)");
    }

    [Test]
    public async Task NoDefault_KeepsRequiredParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Plain
            {
                public string Echo(string value) => value;
            }
            """
        );

        var output = result["app/plain.ts"];
        // Tighter assertion than `DoesNotContain("=")`: the signature
        // ends right after the return-type annotation, with no
        // initializer suffix between `value: string` and the body.
        await Assert.That(output).Contains("echo(value: string): string {");
    }

    [Test]
    public async Task AbstractMethod_DefaultValue_EmitsOptionalWithoutInitializer()
    {
        // TypeScript forbids parameter initializers in abstract method
        // declarations. Drop the `= expr` form but keep the parameter
        // optional via the `?` suffix so callers may omit it.
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public abstract class Renderer
            {
                public abstract string Render(string body, string title = "default");
            }
            """
        );

        var output = result["app/renderer.ts"];
        await Assert.That(output).Contains("abstract render(body: string, title?: string): string");
        await Assert.That(output).DoesNotContain("title: string = ");
    }
}
