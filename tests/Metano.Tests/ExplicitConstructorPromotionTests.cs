namespace Metano.Tests;

/// <summary>
/// A parameter of an explicit constructor is promoted only to an
/// auto-property its body initializes from it (#801): a computed property
/// named like the parameter keeps its getter.
/// </summary>
public class ExplicitConstructorPromotionTests
{
    [Test]
    public async Task ComputedPropertyNamedLikeAParameter_KeepsItsGetter()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class NoInit
            {
                private string _name;
                private System.Func<int>? _f;

                public NoInit(string n, System.Func<int> f) { _f = f; _name = n; }

                public string N => _name + _f!();
            }
            """
        )["no-init.ts"];

        await Assert.That(output).Contains("  constructor(n: string, f: () => number) {");
        await Assert.That(output).Contains("  get n(): string {");
        await Assert.That(output).DoesNotContain("declare readonly n:");
    }

    [Test]
    public async Task AutoPropertyTheBodyInitializes_IsPromoted()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Named
            {
                public Named(string name) { Name = name; }

                public string Name { get; }
            }
            """
        )["named.ts"];

        await Assert.That(output).Contains("  declare readonly name: string;");
        await Assert.That(output).Contains("    this.name = name;");
    }

    [Test]
    public async Task AutoPropertyTheBodyDoesNotInitialize_IsNotPromoted()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                public Counter(int count) { System.Console.WriteLine(count); }

                public int Count { get; set; }
            }
            """
        )["counter.ts"];

        await Assert.That(output).Contains("    this.count = 0;");
        await Assert.That(output).DoesNotContain("this.count = count;");
    }
}
