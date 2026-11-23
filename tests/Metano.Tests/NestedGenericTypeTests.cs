namespace Metano.Tests;

/// <summary>
/// A class, struct or interface nested in a generic type uses the container's type
/// parameters without declaring them. Lowered outside its container, it declares them
/// ahead of its own, and every reference passes the container's type arguments ahead
/// of its own (#436).
/// </summary>
public class NestedGenericTypeTests
{
    private const string Source = """
        [assembly: TranspileAssembly]

        public class Gen<T>
        {
            public T Value { get; set; }

            public class Sub : Gen<T> { }

            public enum Kind { A, B }

            public interface IView { T Read(); }

            public Sub Make() => new Sub();
        }

        public class Outer<A>
        {
            public class Mid<B> { public class Leaf { public A X { get; set; } public B Y { get; set; } } }
        }

        public static class UseGen
        {
            public static int Run()
            {
                var s = new Gen<int>.Sub();
                Gen<int>.Sub t = s;
                var l = new Outer<int>.Mid<string>.Leaf();
                return t.Value + l.X;
            }
        }
        """;

    [Test]
    public async Task NestedType_DeclaresTheContainerTypeParameters()
    {
        var output = TranspileHelper.Transpile(Source)["gen.ts"];

        await Assert.That(output).Contains("class Gen$Sub<T> extends Gen<T> {");
        await Assert.That(output).Contains("interface Gen$IView<T> {");
        await Assert.That(output).Contains("export type Sub<T> = Gen$Sub<T>;");
        await Assert.That(output).Contains("export type Kind = Gen$Kind;");
        await Assert.That(output).Contains("make(): Gen$Sub<T> {");
        await Assert.That(output).Contains("return new Gen$Sub<T>();");
    }

    [Test]
    public async Task DeeplyNestedType_DeclaresEveryContainerTypeParameter()
    {
        var output = TranspileHelper.Transpile(Source)["outer.ts"];

        await Assert.That(output).Contains("class Outer$Mid$Leaf<A, B> {");
        await Assert.That(output).Contains("export type Leaf<A, B> = Outer$Mid$Leaf<A, B>;");
    }

    [Test]
    public async Task ConstructedReference_PassesTheContainerTypeArguments()
    {
        var output = TranspileHelper.Transpile(Source)["use-gen.ts"];

        await Assert.That(output).Contains("const s = new Gen.Sub<number>();");
        await Assert.That(output).Contains("new Outer.Mid.Leaf<number, string>()");
    }
}
