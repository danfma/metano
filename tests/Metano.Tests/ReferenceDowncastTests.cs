namespace Metano.Tests;

/// <summary>
/// An explicit reference conversion <c>(Derived)value</c> takes the target
/// type in TypeScript and, where the target has a runtime class, is checked
/// like .NET checks it (#669).
/// </summary>
public class ReferenceDowncastTests
{
    private const string Shapes = """
        [Transpile]
        public abstract record Shape;

        [Transpile]
        public sealed record Circle(int Radius) : Shape;

        [Transpile]
        public sealed record Box<T>(T Value);

        [Transpile]
        public interface IMarker { }

        [Transpile]
        public sealed class Marked : IMarker { public int N => 3; }
        """;

    [Test]
    public async Task DowncastToAClass_IsCheckedAndTyped()
    {
        var output = TranspileUse(
            """
            public static int RadiusOf(Shape shape)
            {
                var circle = (Circle)shape;

                return circle.Radius;
            }

            public static int Inline(Shape shape) => ((Circle)shape).Radius;
            """
        );

        await Assert.That(output).Contains("const circle = castTo<Circle>(shape, Circle);");
        await Assert.That(output).Contains("return castTo<Circle>(shape, Circle).radius;");
        await Assert.That(output).Contains("import { castTo } from \"metano-runtime\";");
        await Assert.That(output).Contains("import { Circle } from \"./circle\";");
    }

    [Test]
    public async Task DowncastToAGenericClass_CarriesTheTypeArguments()
    {
        var output = TranspileUse("public static int Gen(object o) => ((Box<int>)o).Value;");

        await Assert.That(output).Contains("castTo<Box<number>>(o, Box).value");
    }

    [Test]
    public async Task DowncastToABclException_ChecksItsRuntimeClass()
    {
        var output = TranspileUse(
            """
            public static ArgumentException Narrow(Exception e) => (ArgumentException)e;

            public static Exception Widen(object o) => (Exception)o;
            """
        );

        await Assert.That(output).Contains("castTo<ArgumentException>(e, ArgumentException)");
        await Assert.That(output).Contains("castTo<Exception>(o, Error)");
    }

    /// <summary>
    /// A target with no runtime class but a runtime type descriptor (an
    /// interface, a <c>string</c>) is checked through the descriptor (spec 011
    /// D29); a type parameter, which has none, is only asserted.
    /// </summary>
    [Test]
    public async Task DowncastToAnInterfaceOrPrimitive_ChecksTheDescriptor()
    {
        var output = TranspileUse(
            """
            public static IMarker ToInterface(object o) => (IMarker)o;

            public static string ToText(object o) => (string)o;

            public static T ToParameter<T>(object o) where T : class => (T)o;
            """
        );

        await Assert.That(output).Contains("return castTo<IMarker>(o, IMarker$runtimeType);");
        await Assert.That(output).Contains("return castTo<string>(o, StringType);");
        await Assert.That(output).Contains("return o as T;");
    }

    /// <summary>
    /// An upcast, and a cast whose operand already has the type, keep the bare
    /// operand.
    /// </summary>
    [Test]
    public async Task Upcast_KeepsTheBareOperand()
    {
        var output = TranspileUse("public static Shape Up(Circle circle) => (Shape)circle;");

        await Assert.That(output).Contains("return circle;");
        await Assert.That(output).DoesNotContain("castTo");
    }

    /// <summary>
    /// A cast an earlier type test of the same operand guards keeps the bare
    /// operand, which TypeScript's own narrowing types; a test of another
    /// operand or type does not.
    /// </summary>
    [Test]
    public async Task DowncastGuardedByATypeTest_KeepsTheBareOperand()
    {
        var output = TranspileUse(
            """
            public static bool And(object o) => o is Circle && ((Circle)o).Radius > 0;

            public static int Ternary(object o) => o is Circle c ? ((Circle)o).Radius : 0;

            public static int If(Shape s)
            {
                if (s is Circle { Radius: > 0 } && s != null)
                {
                    return ((Circle)s).Radius;
                }

                return 0;
            }

            public static bool Other(object o, object p) => p is Circle && ((Circle)o).Radius > 0;
            """
        );

        await Assert.That(output).Contains("o instanceof Circle && o.radius > 0");
        await Assert.That(output).Contains("(c = o, true) ? o.radius : 0");
        await Assert.That(output).Contains("return s.radius;");

        await Assert
            .That(output)
            .Contains("p instanceof Circle && castTo<Circle>(o, Circle).radius > 0");
    }

    /// <summary>
    /// A cast inside a type its target derives from cannot import the target's
    /// class back (the target's module imports this one to extend it, a
    /// load-time import cycle), so it only types the result.
    /// </summary>
    [Test]
    public async Task DowncastInsideTheBaseType_OnlyTypesTheResult()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Shape
            {
                public int Radius() => ((Circle)this).Size;
            }

            [Transpile]
            public sealed class Circle : Shape
            {
                public int Size => 2;
            }
            """
        );

        await Assert.That(files["shape.ts"]).Contains("(this as Circle).size");
        await Assert.That(files["shape.ts"]).DoesNotContain("castTo");
        await Assert.That(files["shape.ts"]).Contains("import type { Circle }");
    }

    /// <summary>
    /// A cast whose class import would close a cycle of value imports through
    /// a third module (<c>node</c> → <c>printer</c> → <c>leaf</c> → <c>node</c>)
    /// only types the result: loading <c>node</c> first would otherwise run
    /// <c>class Leaf extends Node</c> before <c>Node</c> exists. A cast in a
    /// module off the cycle keeps its check.
    /// </summary>
    [Test]
    public async Task DowncastWhoseImportClosesACycle_OnlyTypesTheResult()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Node
            {
                public string Describe() => Printer.Print(this);
            }

            [Transpile]
            public static class Printer
            {
                public static string Print(Node n) => "leaf:" + ((Leaf)n).Text;
            }

            [Transpile]
            public class Leaf : Node
            {
                public string Text = "t";
            }

            [Transpile]
            public static class Reader
            {
                public static string Read(Node n) => ((Leaf)n).Text;
            }
            """
        );

        await Assert.That(files["printer.ts"]).Contains("(n as Leaf).text");
        await Assert.That(files["printer.ts"]).DoesNotContain("castTo");
        await Assert.That(files["printer.ts"]).Contains("import type { Leaf } from \"./leaf\";");
        await Assert.That(files["reader.ts"]).Contains("castTo<Leaf>(n, Leaf).text");
    }

    private static string TranspileUse(string members) =>
        TranspileHelper.Transpile(
            $$"""
            using Metano.Annotations;

            {{Shapes}}

            [Transpile]
            public static class Use
            {
                {{members}}
            }
            """
        )["use.ts"];
}
