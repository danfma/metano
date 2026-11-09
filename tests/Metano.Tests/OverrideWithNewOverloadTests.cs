using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A derived type that overrides an overload of an inherited dispatched group
/// and adds a new one (#564, spec 013 FR-024, FR-025): the override stands
/// for its root in the visible group exactly once, so the new overload is
/// named against the inherited names and nothing dead-ends.
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

            public override string Add(string s) => "leaf:" + base.Add(s);

            public override string Add(double d) => "leaf:" + base.Add(d);

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

        [Transpile]
        public class Renamed : Base
        {
            public override string Add(int y) => "renamed";

            public string Add(bool y) => "bool";
        }

        [Transpile]
        public class RenamedClash : Base
        {
            public override string Add(int y) => "renamed";

            public string Add(bool x) => "bool";
        }

        [Transpile]
        public class Mapper
        {
            public virtual string Map<T>(T x) => "generic";

            public virtual string Map(int x, int y) => "pair";
        }

        [Transpile]
        public class FlagMapper : Mapper
        {
            public override string Map<T>(T x) => "flag:" + base.Map(x);

            public string Map(bool b, bool c) => "flags";
        }
        """;

    private const string BaseAndHider = """
        [Transpile]
        public class Base
        {
            public virtual string Add(int x) => "int";

            public virtual string Add(string s) => "string";
        }

        [Transpile]
        public class Hider : Base
        {
            public new string Add(int x) => "hidden";

            public string Add(bool b) => "bool";
        }
        """;

    private const string HiddenFastPathCollision =
        "Fast path 'Hider.addX' of 'Add(int x)' collides with the fast path of 'Add(int x)' "
        + "inherited from 'Base'; it is emitted as 'addX$' (spec 013 FR-004).";

    private const string RootLibrary = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("lib")]

        namespace Lib;

        public class Base
        {
            public virtual string Add(int x) => "int";

            public virtual string Add(string s) => "string";

            public virtual string Map<T>(T x) => "generic";

            public virtual string Map(int x, int y) => "pair";
        }
        """;

    private const string RootConsumer = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        namespace App;

        public class Derived : Lib.Base
        {
            public override string Add(int x) => "derived";

            public string Add(bool b) => "bool";

            public override string Map<T>(T x) => "derived";

            public string Map(bool b, bool c) => "flags";
        }
        """;

    /// <summary>
    /// An override, an override of an override, an abstract and a generic
    /// base, a generic method, an override that renames its parameter and an
    /// interface implementer, each next to a new overload: no report.
    /// </summary>
    [Test]
    public async Task TypeScript_NamesTheNewOverloadAgainstTheInheritedOnes_WithNoDeadEnd()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Shapes);

        await TranspileHelper.AssertGolden("overload-override-plus-overload.ts", Joined(files));
        await Assert.That(MessagesOf(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task Dart_NamesTheNewOverloadAgainstTheInheritedOnes_WithNoDeadEnd()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(Shapes);

        await TranspileHelper.AssertGolden("overload-override-plus-overload.dart", Joined(files));
        await Assert.That(MessagesOf(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A <c>new</c> method is its own root: it stays in the visible group,
    /// hides the base overload of its signature there and collides with the
    /// fast path it hides (FR-004), the only report.
    /// </summary>
    [Test]
    public async Task TypeScript_NewMethodNextToANewOverload_IsItsOwnRoot()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(BaseAndHider);

        await TranspileHelper.AssertGolden(
            "overload-override-plus-overload-hider.ts",
            files["hider.ts"]
        );

        await Assert.That(MessagesOf(diagnostics)).IsEquivalentTo([HiddenFastPathCollision]);
    }

    [Test]
    public async Task Dart_NewMethodNextToANewOverload_IsItsOwnRoot()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(BaseAndHider);

        await TranspileHelper.AssertGolden(
            "overload-override-plus-overload-hider.dart",
            files["hider.dart"]
        );

        await Assert
            .That(MessagesOf(diagnostics))
            .IsEquivalentTo([$"Dart target: {HiddenFastPathCollision}"]);
    }

    /// <summary>The roots come from another assembly: same names, no report.</summary>
    [Test]
    public async Task CrossAssemblyRoots_NameTheNewOverloadTheSame()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            RootLibrary,
            RootConsumer
        );

        await TranspileHelper.AssertGolden(
            "overload-override-plus-overload-cross-package.ts",
            files["app/derived.ts"]
        );

        await Assert.That(MessagesOf(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The override of a base overload that also implements a multiply
    /// constructed interface is emitted under the interface root's name
    /// (<c>qQ</c>, DECIDED-30), while the visible group names the base
    /// overload it stands for (<c>Bq</c>): the new overload computes
    /// <c>qQ</c> too and the claim pass moves it to <c>qQ$</c>, reported.
    /// </summary>
    [Test]
    public async Task OverrideEmittedUnderAnInterfaceRootName_LeavesTheClashToTheClaimPass()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IQ<T>
            {
                [Name("q")]
                string Q(T a);

                string Q(T a, int n);
            }

            [Transpile]
            public class QB
            {
                [Name("bq")]
                public virtual string Q(int z) => "bi";

                public virtual string Q(int z, int n) => "bin";

                public virtual string Q(string s) => "bs";

                public virtual string Q(string s, int m) => "bsm";
            }

            [Transpile]
            public class QD : QB, IQ<int>, IQ<string>
            {
                public override string Q(int z) => "di";

                public string Q(bool q) => "db";
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "overload-override-plus-overload-interface-root.ts",
            files["qd.ts"]
        );

        await Assert
            .That(MessagesOf(diagnostics))
            .IsEquivalentTo([
                "'QD' implements 'IQ<T>' more than once; calls to 'Q' keep runtime dispatch "
                    + "(spec 013 FR-036).",
                "Fast path 'QD.qQ' of 'Q(bool q)' collides with the fast path of 'Q(int z)'; it "
                    + "is emitted as 'qQ$' (spec 013 FR-004).",
            ]);
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
}
