namespace Metano.Tests;

public class CtorFieldEmissionTests
{
    [Test]
    public async Task PrimaryCtor_NumericCapturedField_OmitsRedundantZeroInitializer()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Column(int gap, string label)
            {
                public int Sum() => gap + label.Length;
            }
            """
        );

        var output = result["app/column.ts"];
        await Assert.That(output).Contains("private readonly _gap: number;");
        await Assert.That(output).DoesNotContain("private readonly _gap: number = 0");
        await Assert.That(output).Contains("this._gap = gap;");
    }

    [Test]
    public async Task PrimaryCtor_BooleanCapturedField_OmitsRedundantFalseInitializer()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Toggle(bool active)
            {
                public bool Read() => active;
            }
            """
        );

        var output = result["app/toggle.ts"];
        await Assert.That(output).Contains("private readonly _active: boolean;");
        await Assert.That(output).DoesNotContain("_active: boolean = false");
    }

    [Test]
    public async Task ExplicitCtor_ParameterDefaultValue_PreservedInTypeScript()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Heading
            {
                private readonly string _content;
                private readonly int _level;

                public Heading(string content, int level = 1)
                {
                    _content = content;
                    _level = level;
                }
            }
            """
        );

        var output = result["app/heading.ts"];
        await Assert.That(output).Contains("constructor(content: string, level: number = 1)");
    }

    [Test]
    public async Task ExplicitCtor_StringDefault_PreservedInTypeScript()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public sealed class Greeter
            {
                private readonly string _greeting;

                public Greeter(string greeting = "hello")
                {
                    _greeting = greeting;
                }
            }
            """
        );

        var output = result["app/greeter.ts"];
        await Assert.That(output).Contains("constructor(greeting: string = \"hello\")");
    }
}
