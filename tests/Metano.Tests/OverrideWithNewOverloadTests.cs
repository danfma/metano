using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A derived type that overrides an overload of an inherited dispatched group
/// and adds a new one (#564, spec 013 FR-024, FR-025): the override stands
/// for its root in the visible group exactly once, so the new overload is
/// named against the inherited names and nothing dead-ends. The only report
/// left is the real one: the <c>new</c> method, its own root, collides with
/// the fast path it hides (FR-004). The TypeScript golden type-checks under
/// <c>tsgo --strict --noImplicitOverride</c>.
/// </summary>
public class OverrideWithNewOverloadTests
{
    private const string Shapes = """
        [Transpile]
        public class Base
        {
            public virtual string Add(int x) => "int";

            public virtual string Add(string s) => "string";
        }

        [Transpile]
        public class Derived : Base
        {
            public override string Add(int x) => "derived:" + base.Add(x);

            public string Add(bool b) => "bool";
        }

        [Transpile]
        public class Clash : Base
        {
            public override string Add(string s) => "clash";

            public string Add(long x) => "long";
        }

        [Transpile]
        public class Middle : Base
        {
            public override string Add(string s) => "middle";

            public virtual string Add(double d) => "double";
        }

        [Transpile]
        public class Leaf : Middle
        {
            public override string Add(int x) => "leaf";

            public override string Add(double d) => "leaf:" + base.Add(d);

            public string Add(bool b) => "bool";
        }

        [Transpile]
        public class Hider : Base
        {
            public new string Add(int x) => "hidden";

            public string Add(bool b) => "bool";
        }

        [Transpile]
        public abstract class Shape
        {
            public abstract string Draw(int x);

            public abstract string Draw(string s);
        }

        [Transpile]
        public class Circle : Shape
        {
            public override string Draw(int x) => "int";

            public override string Draw(string s) => "string";

            public string Draw(bool b) => "bool";
        }

        [Transpile]
        public class Repo<T>
        {
            public virtual string Put(T item) => "item";

            public virtual string Put(int count) => "count";
        }

        [Transpile]
        public class StrRepo : Repo<string>
        {
            public override string Put(string item) => "str:" + item;

            public string Put(bool flag) => "flag";
        }

        [Transpile]
        public interface ISink
        {
            string Write(int x);

            string Write(string s);
        }

        [Transpile]
        public class Sink : ISink
        {
            public string Write(int x) => "int";

            public string Write(string s) => "string";

            public string Write(bool b) => "bool";
        }
        """;

    private const string HiddenFastPathCollision =
        "Fast path 'Hider.addX' of 'Add(int x)' collides with the fast path of 'Add(int x)' "
        + "inherited from 'Base'; it is emitted as 'addX$' (spec 013 FR-004).";

    [Test]
    public async Task TypeScript_NamesTheNewOverloadAgainstTheInheritedOnes_WithNoDeadEnd()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Shapes);

        await AssertGolden("overload-override-plus-overload.ts", Joined(files));
        await Assert.That(MessagesOf(diagnostics)).IsEquivalentTo([HiddenFastPathCollision]);
    }

    [Test]
    public async Task Dart_NamesTheNewOverloadAgainstTheInheritedOnes_WithNoDeadEnd()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(Shapes);

        await AssertGolden("overload-override-plus-overload.dart", Joined(files));

        await Assert
            .That(MessagesOf(diagnostics))
            .IsEquivalentTo([$"Dart target: {HiddenFastPathCollision}"]);
    }

    private static List<string> MessagesOf(IReadOnlyList<MetanoDiagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => diagnostic.Message).ToList();

    private static string Joined(Dictionary<string, string> files) =>
        string.Join(
            "\n",
            files
                .OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => $"// {file.Key}\n{file.Value}")
        );

    private static async Task AssertGolden(string name, string actual)
    {
        if (TranspileHelper.UpdateGoldensRequested)
        {
            TranspileHelper.WriteExpected(name, actual);

            return;
        }

        await Assert.That(actual).IsEqualTo(TranspileHelper.ReadExpected(name));
    }
}
