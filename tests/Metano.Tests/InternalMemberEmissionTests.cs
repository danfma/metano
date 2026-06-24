namespace Metano.Tests;

public class InternalMemberEmissionTests
{
    [Test]
    public async Task InternalMethod_EmittedAsPublicOnTypeScript()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Widget
            {
                internal void Bind(int context) { }
            }
            """
        );

        var output = result["app/widget.ts"];
        await Assert.That(output).Contains("bind(context: number): void");
    }

    [Test]
    public async Task InternalProperty_EmittedAsPublicOnTypeScript()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Widget
            {
                internal int Count { get; set; }
            }
            """
        );

        var output = result["app/widget.ts"];
        await Assert.That(output).Contains("count");
    }

    [Test]
    public async Task InternalField_EmittedOnTypeScript()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Widget
            {
                internal int _flag;
            }
            """
        );

        var output = result["app/widget.ts"];
        await Assert.That(output).Contains("_flag");
    }
}
