namespace Metano.Tests;

/// <summary>
/// A non-public nested type that extends another emitted non-public nested type keeps
/// its <c>extends</c> clause, and the module-local declarations are ordered so a base
/// is declared before the class that extends it (#422).
/// </summary>
public class NonPublicNestedBaseTests
{
    [Test]
    public async Task PrivateNestedRecord_ExtendsItsPrivateNestedBase()
    {
        var output = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class Sibs2
            {
                private sealed record B(int X) : A(X);
                private abstract record A(int X) { public int Twice() => X * 2; }
                internal class C : D { }
                internal class D { public int Y => 5; }
                public static int Run() => new B(3).Twice() + new C().Y;
            }
            """
        )["sibs2.ts"];

        await Assert.That(output).Contains("class Sibs2$B extends Sibs2$A {");
        await Assert.That(output).Contains("class Sibs2$C extends Sibs2$D {");

        await Assert
            .That(output.IndexOf("abstract class Sibs2$A", StringComparison.Ordinal))
            .IsLessThan(output.IndexOf("class Sibs2$B", StringComparison.Ordinal));

        await Assert
            .That(output.IndexOf("class Sibs2$D", StringComparison.Ordinal))
            .IsLessThan(output.IndexOf("class Sibs2$C", StringComparison.Ordinal));
    }
}
