namespace Metano.Tests;

/// <summary>
/// The root-anchored fast paths of spec 013 slice 013d (FR-015, FR-016,
/// FR-025, FR-030, FR-031, FR-036, FR-037), pinned as goldens that compile
/// under <c>tsgo --strict --noImplicitOverride --declaration</c>. A fast path
/// is an addressable member under its root's name, with its overload's
/// visibility; an override overrides it and inherits the root's dispatcher,
/// an interface declares it and its implementers emit it.
/// </summary>
public class OverloadVirtualGoldenTests
{
    /// <summary>
    /// A partial and a full override (no dispatcher), <c>base.Add</c> as
    /// <c>super.addX</c>, a <c>new</c> hiding method (<c>addX$</c>), and two
    /// types adding an overload to a group with a <c>private</c> base
    /// overload: the <c>override</c> superset dispatcher, one of them named
    /// against the base's <c>private addS</c> (<c>addS$</c>).
    /// </summary>
    [Test]
    public async Task VirtualGroups_OverrideTheRootsFastPaths()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Base
            {
                public virtual int Add(int x) => x;

                public virtual int Add(string s) => s.Length;
            }

            [Transpile]
            public class Partial : Base
            {
                public override int Add(int x) => base.Add(x) * 2;

                public int Twice(int x) => base.Add(x) + Add(x);
            }

            [Transpile]
            public class Full : Base
            {
                public override int Add(int x) => x + 1;

                public override int Add(string s) => base.Add(s) + 1;
            }

            [Transpile]
            public class Hider : Base
            {
                public new int Add(int x) => 100;
            }

            [Transpile]
            public class Ledger
            {
                public int Add(int x) => x;

                private int Add(string s) => s.Length + 1000;

                public int Count(string s) => Add(s);
            }

            [Transpile]
            public class Flagged : Ledger
            {
                public int Add(bool flag) => flag ? 1 : 0;
            }

            [Transpile]
            public class Shorty : Ledger
            {
                public int Add(short s) => 7;
            }
            """
        );

        await Assert
            .That(Joined(result, "base", "partial", "full", "hider", "ledger", "flagged", "shorty"))
            .IsEqualTo(TranspileHelper.ReadExpected("overload-virtual.ts"));
    }

    /// <summary>
    /// A generic root, overridden through a closed base: the names are the
    /// root's (<c>putItem</c>, <c>fT</c>), whatever the construction.
    /// </summary>
    [Test]
    public async Task GenericRoots_KeepTheirNamesInClosedOverrides()
    {
        var result = TranspileHelper.Transpile(
            """
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
            }

            [Transpile]
            public class Box<T>
            {
                public virtual string F(T a) => "T";

                public virtual string F(int a) => "int";
            }

            [Transpile]
            public class StrBox : Box<string>
            {
                public override string F(string a) => "S:" + a + base.F(a);
            }
            """
        );

        await Assert
            .That(Joined(result, "repo", "str-repo", "box", "str-box"))
            .IsEqualTo(TranspileHelper.ReadExpected("overload-virtual-generic.ts"));
    }

    /// <summary>
    /// DECIDED-29: an inherited non-overloaded virtual that a derived type
    /// overloads keeps today's emission — its override and the new overload
    /// stay apart, with no dispatcher.
    /// </summary>
    [Test]
    public async Task InheritedSingleVirtual_KeepsTodaysEmission()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Widget
            {
                public abstract string Build();
            }

            [Transpile]
            public abstract class StatefulWidget : Widget
            {
                protected abstract Widget Build(int context);

                public override string Build() => "ok";
            }
            """
        );

        await Assert
            .That(Joined(result, "widget", "stateful-widget"))
            .IsEqualTo(TranspileHelper.ReadExpected("overload-virtual-single.ts"));
    }

    /// <summary>
    /// DECIDED-28: a base read from metadata only is named by its producer's
    /// whole pipeline over the metadata symbols — its <c>protected</c> field
    /// <c>addS</c> moves the root's fast path to <c>addS$</c>, which the
    /// consumer's override reuses — and a cross-assembly <c>protected
    /// override</c> of a <c>protected internal</c> root is public, as the
    /// root. A project reference names them the same.
    /// </summary>
    [Test]
    public async Task MetadataBase_NamesItsRootsFromTheProducersPipeline()
    {
        var library = VirtualLibrary("protected");
        var fromImage = TranspileHelper.TranspileWithLibraryImage(library, VirtualConsumer);
        var fromProject = TranspileHelper.TranspileWithLibrary(library, VirtualConsumer);

        await Assert
            .That(fromImage["app/derived.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("overload-virtual-metadata.ts"));
        await Assert.That(fromProject["app/derived.ts"]).IsEqualTo(fromImage["app/derived.ts"]);
    }

    /// <summary>
    /// DECIDED-28's blind spot: Roslyn imports no <c>private</c> member of a
    /// metadata reference (<c>MetadataImportOptions.Public</c>), from an
    /// implementation or a reference assembly alike, so a private claimant
    /// that moved the producer's fast path to <c>addS$</c> is missed and the
    /// consumer overrides <c>addS</c>, which <c>tsgo</c> rejects with TS4117
    /// naming <c>addS$</c>. A project reference sees it.
    /// </summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MetadataBase_MissesAPrivateClaimant(bool referenceAssembly)
    {
        var library = VirtualLibrary("private");

        var fromImage = TranspileHelper.TranspileWithLibraryImage(
            library,
            VirtualConsumer,
            referenceAssembly
        );

        var fromProject = TranspileHelper.TranspileWithLibrary(library, VirtualConsumer);

        await Assert
            .That(fromImage["app/derived.ts"])
            .Contains("override addS(s: string): number {");
        await Assert
            .That(fromProject["app/derived.ts"])
            .Contains("override addS$(s: string): number {");
    }

    /// <summary>
    /// FR-030: abstract overloads declare <c>abstract</c> fast paths beside
    /// the concrete dispatcher (today <c>MS0001</c>), and a subclass overrides
    /// them.
    /// </summary>
    [Test]
    public async Task AbstractGroups_DeclareAbstractFastPathsAndTheDispatcher()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Op
            {
                public abstract int Run(int x);

                public abstract int Run(string s);

                public int Run(bool flag) => flag ? Run(1) : Run("ab");
            }

            [Transpile]
            public class Neg : Op
            {
                public override int Run(int x) => -x;

                public override int Run(string s) => -s.Length;
            }
            """
        );

        await Assert
            .That(Joined(result, "op", "neg"))
            .IsEqualTo(TranspileHelper.ReadExpected("overload-abstract-methods.ts"));
    }

    /// <summary>
    /// FR-031, FR-037: an interface declares its fast-path signatures; an
    /// implementer emits them under the root's names with its own parameter
    /// names; explicit implementations beside a class overload share one
    /// dispatcher; a method implementing two roots forwards the second name.
    /// </summary>
    [Test]
    public async Task InterfaceGroups_DeclareFastPathSignaturesTheImplementersEmit()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IShape
            {
                int Area(int s);

                int Area(int w, int h);
            }

            [Transpile]
            public class Sq : IShape
            {
                public int Area(int side) => side * side;

                public int Area(int w, int h) => w * h;
            }

            [Transpile]
            public class Plot : IShape
            {
                int IShape.Area(int s) => s * s;

                int IShape.Area(int w, int h) => w * h;

                public int Area(bool big) => big ? 1 : 0;
            }

            [Transpile]
            public interface IA
            {
                int G(int a);

                int G(string s);
            }

            [Transpile]
            public interface IB
            {
                int G(int b);

                int G(bool f);
            }

            [Transpile]
            public class Both : IA, IB
            {
                public int G(int x) => x + 1;

                public int G(string s) => s.Length;

                public int G(bool f) => f ? 10 : 20;
            }
            """
        );

        await Assert
            .That(Joined(result, "i-shape", "sq", "plot", "ia", "ib", "both"))
            .IsEqualTo(TranspileHelper.ReadExpected("overload-interface.ts"));
    }

    /// <summary>
    /// FR-036: a type implementing one generic interface twice declares each
    /// root name as a runtime dispatcher over its implementations, with no
    /// per-implementation fast path; the C# group's dispatcher calls them.
    /// </summary>
    [Test]
    public async Task MultiplyConstructedInterface_KeepsRuntimeDispatchUnderTheRootNames()
    {
        var result = TranspileHelper.Transpile(ConvSource);

        await Assert
            .That(Joined(result, "i-conv", "c"))
            .IsEqualTo(TranspileHelper.ReadExpected("overload-interface-multiple.ts"));
    }

    /// <summary>
    /// FR-036 inheritance (R15): an override of a multiply-constructed
    /// implementation (<c>D3 : C</c>), and a type adding a construction
    /// (<c>D4 : C1, IConv&lt;string&gt;</c>), declare <c>override</c> root-name
    /// dispatchers with every constructed signature that forward what they do
    /// not implement to <c>super</c>.
    /// </summary>
    [Test]
    public async Task InheritedMultiplyConstructedInterface_OverridesTheRootDispatchers()
    {
        var result = TranspileHelper.Transpile(ConvSource);

        await Assert
            .That(Joined(result, "d3", "c1", "d4"))
            .IsEqualTo(TranspileHelper.ReadExpected("overload-interface-multiple-inherited.ts"));
    }

    internal const string ConvSource = """
        [Transpile]
        public interface IConv<T>
        {
            int Conv(T x);

            int Conv(T x, int n);
        }

        [Transpile]
        public class C : IConv<int>, IConv<string>
        {
            public virtual int Conv(int x) => 1;

            public int Conv(int x, int n) => 3;

            public int Conv(string x) => 2;

            public int Conv(string x, int n) => 4;
        }

        [Transpile]
        public class D3 : C
        {
            public override int Conv(int x) => 10;
        }

        [Transpile]
        public class C1 : IConv<int>
        {
            public int Conv(int x) => 1;

            public int Conv(int x, int n) => 3;
        }

        [Transpile]
        public class D4 : C1, IConv<string>
        {
            public int Conv(string x) => 2;

            public int Conv(string x, int n) => 4;
        }
        """;

    private static string VirtualLibrary(string fieldAccessibility) =>
        $$"""
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace Lib;

            public class Base
            {
                {{fieldAccessibility}} int addS = 1000;

                protected internal virtual int Add(int x) => x;

                public virtual int Add(string s) => s.Length + addS;
            }
            """;

    private const string VirtualConsumer = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        namespace App;

        public class Derived : Lib.Base
        {
            protected override int Add(int x) => x * 2;

            public override int Add(string s) => 3;
        }
        """;

    private static string Joined(Dictionary<string, string> result, params string[] files) =>
        string.Join("\n", files.Select(file => result[file + ".ts"]));
}
