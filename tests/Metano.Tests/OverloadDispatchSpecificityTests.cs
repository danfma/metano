namespace Metano.Tests;

/// <summary>
/// An overload dispatcher tries the overload whose parameter derives from
/// another's first: the guards overlap and the first that matches wins, so a
/// call from JavaScript with the derived value must not take the base arm
/// (#677). C# call sites bind statically and are unaffected.
/// </summary>
public class OverloadDispatchSpecificityTests
{
    [Test]
    public async Task DerivedExceptionArm_IsTriedBeforeTheBaseExceptionArm()
    {
        var output = TranspileHandler(
            """
            public string F(Exception e) => "exception";

            public string F(ArgumentException e) => "argument";
            """
        );

        await Assert.That(output).Contains("instanceof ArgumentException");

        await Assert
            .That(output.IndexOf("instanceof ArgumentException", StringComparison.Ordinal))
            .IsLessThan(output.IndexOf("instanceof Error", StringComparison.Ordinal));
    }

    [Test]
    public async Task DerivedClassArm_IsTriedBeforeTheBaseClassAndInterfaceArms()
    {
        var output = TranspileHandler(
            """
            public string F2(IShape s) => "interface";

            public string F2(Shape s) => "base";

            public string F2(Circle c) => "derived";
            """
        );

        var derived = output.IndexOf("this.f2Circle(", StringComparison.Ordinal);
        var @base = output.IndexOf("this.f2Shape(", StringComparison.Ordinal);
        var @interface = output.IndexOf("this.f2IShape(", StringComparison.Ordinal);

        await Assert.That(derived).IsGreaterThan(-1);
        await Assert.That(derived).IsLessThan(@base);
        await Assert.That(@base).IsLessThan(@interface);
    }

    [Test]
    public async Task ObjectArm_IsTriedLast_AndAcceptsAnyValue()
    {
        var output = TranspileHandler(
            """
            public string G(object o) => "object";

            public string G(Circle c) => "circle";
            """
        );

        await Assert
            .That(output.IndexOf("this.gC(", StringComparison.Ordinal))
            .IsLessThan(output.IndexOf("this.gO(", StringComparison.Ordinal));

        await Assert.That(output).DoesNotContain("typeof args[0] === \"object\")");
    }

    /// <summary>
    /// Overloads whose parameters are unrelated, or only one of two related
    /// ones, keep their declaration order.
    /// </summary>
    [Test]
    public async Task UnrelatedAndCrossedOverloads_KeepDeclarationOrder()
    {
        var output = TranspileHandler(
            """
            public string H(Circle c, Exception e) => "a";

            public string H(Shape s, ArgumentException e) => "b";
            """
        );

        await Assert
            .That(output.IndexOf("this.hCE(", StringComparison.Ordinal))
            .IsLessThan(output.IndexOf("this.hSE(", StringComparison.Ordinal));
    }

    private static string TranspileHandler(string members) =>
        TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public interface IShape { }

            [Transpile]
            public class Shape : IShape { }

            [Transpile]
            public sealed class Circle : Shape { }

            [Transpile]
            public sealed class Handler
            {
                {{members}}
            }
            """
        )["handler.ts"];
}
