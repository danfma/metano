using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Spec 011 stage 011b, hand-in B1: runtime type descriptors for classes,
/// interfaces, enums and primitives, reached only by the code that reads them
/// (D29), and the lowering of <c>is</c> / <c>as</c> / casts / patterns /
/// dispatchers / <c>GetType()</c> / <c>typeof</c> over them. Each golden also
/// runs the bridge's reached-set assertion, which is always on.
/// </summary>
public class RuntimeTypeDescriptorTests
{
    /// <summary>
    /// The factory of US3: a derived instance returned through its base answers
    /// its own descriptor; <c>Blob</c>, which nothing asks about, has none
    /// (contracts/ts-emission.md, "Runtime type tests"). The factory lives in
    /// its own class: a base whose module constructs its subclasses is an ESM
    /// import cycle that fails at load (MS0005).
    /// </summary>
    internal const string FactorySource = """
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Geometry;

        public interface IHasArea
        {
            double Area { get; }
        }

        public abstract class Shape { }

        public static class Shapes
        {
            public static Shape Create(string kind) => kind == "c" ? new Circle() : new Square();
        }

        public class Circle : Shape, IHasArea
        {
            public double Area => 3.0;
        }

        public class Square : Shape { }

        public class Blob
        {
            public int Size => 1;
        }

        public static class Program
        {
            public static string Describe(string kind)
            {
                var s = Shapes.Create(kind);
                var n = s.GetType().Name;
                var same = s.GetType() == typeof(Circle);
                var a = s is IHasArea;
                var c = s as Circle;
                var k = s switch
                {
                    Circle c1 => 1,
                    Square s1 => 2,
                    _ => 0,
                };

                return $"{n} {same} {a} {c != null} {k}";
            }

            public static double AreaOf(Shape s) => ((Circle)s).Area;
        }
        """;

    /// <summary>
    /// Nominal interface tests everywhere (D29): <c>is</c>, a
    /// <c>[GenerateGuard]</c> interface, <c>as</c>, a cast, a pattern and an
    /// overload dispatcher; a reached interface reaches every implementer,
    /// through a base class and a base interface too.
    /// </summary>
    internal const string InterfacesSource = """
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Zoo;

        public interface INamed
        {
            string Name { get; }
        }

        public interface IPet : INamed
        {
            bool Friendly { get; }
        }

        [GenerateGuard]
        public interface ITagged
        {
            string Tag { get; }
        }

        public interface IUnasked
        {
            int X { get; }
        }

        public class Dog : IPet
        {
            public string Name => "dog";

            public bool Friendly => true;
        }

        public class Puppy : Dog { }

        public struct Badge : ITagged
        {
            public string Tag => "badge";
        }

        public class Rock : IUnasked
        {
            public int X => 1;
        }

        public static class Keeper
        {
            public static bool IsNamed(object o) => o is INamed;

            public static bool IsTagged(object o) => o is ITagged;

            public static IPet? AsPet(object o) => o as IPet;

            public static INamed Named(object o) => (INamed)o;

            public static string Greet(INamed named) => named.Name;

            public static string Greet(string text) => text;

            public static string Kind(object o) =>
                o switch
                {
                    IPet => "pet",
                    INamed => "named",
                    _ => "other",
                };
        }
        """;

    /// <summary>
    /// The receiver rules of FR-027 (D29): a class receiver reaches it and its
    /// derived classes, an interface receiver its transpiled implementers, a
    /// constrained type parameter its constraint's rule; <c>object</c>,
    /// <c>Exception</c> and an unconstrained <c>T</c> reach nothing. A value
    /// receiver folds to its descriptor.
    /// </summary>
    internal const string ReceiversSource = """
        using System;
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Receivers;

        public interface IHasArea
        {
            double Area { get; }
        }

        public abstract class Shape { }

        public class Circle : Shape, IHasArea
        {
            public double Area => 1.0;
        }

        public class Square : Shape { }

        public class Gadget : IHasArea
        {
            public double Area => 2.0;
        }

        public class Blob { }

        public struct Point
        {
            public int X;
        }

        public enum Color
        {
            Red,
            Green,
        }

        [PlainObject]
        public record Dto(string Name);

        public static class Probes
        {
            public static string OfObject(object o) => o.GetType().Name;

            public static string OfException(Exception e) => e.GetType().Name;

            public static string OfArea(IHasArea area) => area.GetType().Name;

            public static string OfShape<T>(T shape)
                where T : Shape => shape.GetType().Name;

            public static string OfAnything<T>(T value)
                where T : notnull => value.GetType().Name;

            public static string OfPoint(Point point) => point.GetType().Name;

            public static string OfColor(Color color) => color.GetType().Name;

            public static string OfInt(int number) => number.GetType().Name;
        }
        """;

    /// <summary>
    /// <c>GetType()</c> answered at runtime, <c>typeof</c> of every describable
    /// kind, and the supported <c>System.Type</c> surface (FR-025).
    /// </summary>
    internal const string FallbacksSource = """
        using System;
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Fallbacks;

        public sealed class Token { }

        public class Box<T>
        {
            public Box(T value)
            {
                Value = value;
            }

            public T Value { get; }
        }

        public struct Meter
        {
            public double Value;
        }

        public interface IShape { }

        public enum Mode
        {
            A,
            B,
        }

        public static class Reflect
        {
            private static int Next() => 1;

            public static string OfValue(object o) => o.GetType().FullName ?? "";

            public static string OfCall() => Next().GetType().Name;

            public static string OfSealed(Token token) => token.GetType().Name;

            public static bool IsIntBox(object o) => o.GetType() == typeof(Box<int>);

            public static Type[] Types() =>
                [
                    typeof(int),
                    typeof(string),
                    typeof(int[]),
                    typeof(Action),
                    typeof(object),
                    typeof(IShape),
                    typeof(Mode),
                    typeof(Meter),
                    typeof(InvalidOperationException),
                ];

            public static string Surface(Type type, object o) =>
                $"{type.Name} {type.FullName} {type.Namespace} {type.IsInterface} {type.IsClass} "
                + $"{type.IsInstanceOfType(o)} {type.IsAssignableFrom(typeof(IShape))} "
                + $"{type == typeof(Mode)} {type != typeof(Mode)} {type.Equals(typeof(Meter))} "
                + type.ToString();
        }
        """;

    /// <summary>
    /// Checked casts (FR-026, D29): a class keeps <c>castTo(x, Ctor)</c>, an
    /// interface, an enum and a primitive pass their descriptor, a
    /// <c>decimal</c> its class; arrays, delegates and type parameters are
    /// unchecked (MS0055).
    /// </summary>
    internal const string CastsSource = """
        using System;
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Casts;

        public interface IHasArea
        {
            double Area { get; }
        }

        public class Shape { }

        public class Circle : Shape, IHasArea
        {
            public double Area => 1.0;
        }

        public struct Meter
        {
            public double Value;
        }

        public enum Color
        {
            Red,
            Green,
        }

        [Flags]
        public enum Perms
        {
            None = 0,
            Read = 1,
            Write = 2,
        }

        public static class Conversions
        {
            public static Circle ToCircle(Shape s) => (Circle)s;

            public static IHasArea ToArea(object o) => (IHasArea)o;

            public static string ToText(object o) => (string)o;

            public static int ToInt(object o) => (int)o;

            public static long ToLong(object o) => (long)o;

            public static decimal ToDecimal(object o) => (decimal)o;

            public static Color ToColor(object o) => (Color)o;

            public static Perms ToPerms(object o) => (Perms)o;

            public static Meter ToMeter(object o) => (Meter)o;

            public static object[] ToObjects(object o) => (object[])o;

            public static int[] ToInts(object o) => (int[])o;

            public static Action ToAction(object o) => (Action)o;

            public static T ToParameter<T>(object o) => (T)o;

            public static IHasArea? AsArea(object o) => o as IHasArea;

            public static int? AsInt(object o) => o as int?;
        }
        """;

    [Test]
    public async Task Factory_MatchesTheGolden() =>
        await TranspileHelper.AssertGolden("runtime-types-factory.ts", TranspileAll(FactorySource));

    [Test]
    public async Task Factory_ReachesTheTestedTypesOnly()
    {
        var files = TranspileHelper.Transpile(FactorySource);

        await Assert
            .That(files["geometry/circle.ts"])
            .Contains("static override get $runtimeType()");

        await Assert
            .That(files["geometry/square.ts"])
            .Contains("static override get $runtimeType()");

        await Assert.That(files["geometry/shape.ts"]).Contains("static get $runtimeType()");
        await Assert.That(files["geometry/blob.ts"]).DoesNotContain("$runtimeType");
        await Assert.That(files["geometry/i-has-area.ts"]).Contains("IHasArea$runtimeType");
    }

    [Test]
    public async Task Interfaces_MatchesTheGolden() =>
        await TranspileHelper.AssertGolden(
            "runtime-types-interfaces.ts",
            TranspileAll(InterfacesSource)
        );

    [Test]
    public async Task Interfaces_AnUnaskedInterfaceAndItsImplementerHaveNoDescriptor()
    {
        var files = TranspileHelper.Transpile(InterfacesSource);

        await Assert.That(files["zoo/i-unasked.ts"]).DoesNotContain("$runtimeType");
        await Assert.That(files["zoo/rock.ts"]).DoesNotContain("$runtimeType");
        await Assert.That(files["zoo/puppy.ts"]).Contains("static override get $runtimeType()");
    }

    [Test]
    public async Task GetTypeReceivers_MatchesTheGolden() =>
        await TranspileHelper.AssertGolden("get-type-receivers.ts", TranspileAll(ReceiversSource));

    [Test]
    public async Task GetTypeReceivers_ReachByTheReceiverRules()
    {
        var files = TranspileHelper.Transpile(ReceiversSource);

        await Assert.That(files["receivers/circle.ts"]).Contains("get $runtimeType()");
        await Assert.That(files["receivers/gadget.ts"]).Contains("get $runtimeType()");
        await Assert.That(files["receivers/square.ts"]).Contains("get $runtimeType()");
        await Assert.That(files["receivers/shape.ts"]).Contains("get $runtimeType()");
        await Assert.That(files["receivers/blob.ts"]).DoesNotContain("$runtimeType");
        await Assert.That(files["receivers/i-has-area.ts"]).DoesNotContain("$runtimeType");
    }

    [Test]
    public async Task GetTypeFallbacks_MatchesTheGolden() =>
        await TranspileHelper.AssertGolden("get-type-fallbacks.ts", TranspileAll(FallbacksSource));

    [Test]
    public async Task CheckedCasts_MatchesTheGolden() =>
        await TranspileHelper.AssertGolden("checked-casts.ts", TranspileAll(CastsSource));

    [Test]
    public async Task NoRuntimeQuestions_MatchesTheGolden() =>
        await TranspileHelper.AssertGolden(
            "nfr-002-no-questions.ts",
            Concatenated(Fixtures.NoRuntimeQuestionsFixtureTests.TranspileFixture())
        );

    [Test]
    public async Task NoRuntimeQuestions_DeclaresNoDescriptor()
    {
        var output = Concatenated(Fixtures.NoRuntimeQuestionsFixtureTests.TranspileFixture());

        await Assert.That(output).DoesNotContain("$runtimeType");
        await Assert.That(output).DoesNotContain("$rt");
        await Assert.That(output).DoesNotContain("ClassType");
    }

    [Test]
    public async Task EmitRuntimeType_OnAType_ForcesItsDescriptor()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Forced;

            [EmitRuntimeType]
            public interface IFoo { }

            public interface IBar { }

            [EmitRuntimeType]
            public class Widget : IFoo { }
            """
        );

        await Assert.That(files["forced/i-foo.ts"]).Contains("export const IFoo$runtimeType");
        await Assert.That(files["forced/i-bar.ts"]).DoesNotContain("$runtimeType");
        await Assert.That(files["forced/widget.ts"]).Contains("() => IFoo$runtimeType");
        await Assert.That(files["forced/index.ts"]).Contains("IFoo$runtimeType");
    }

    [Test]
    public async Task EmitRuntimeType_OnTheAssembly_ForcesEveryPublicType()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitRuntimeType]

            namespace Forced;

            public interface IFoo { }

            public enum Level { Low, High }

            public class Widget { }
            """
        );

        await Assert.That(files["forced/i-foo.ts"]).Contains("export const IFoo$runtimeType");
        await Assert.That(files["forced/level.ts"]).Contains("export const Level$runtimeType");
        await Assert.That(files["forced/widget.ts"]).Contains("static get $runtimeType()");
    }

    [Test]
    public async Task FlagsEnum_TestsTheUnderlyingRange()
    {
        var files = TranspileHelper.Transpile(CastsSource);

        await Assert
            .That(files["casts/perms.ts"])
            .Contains("test: (v: unknown): v is Perms => isInt32(v),");
    }

    [Test]
    public async Task NestedClass_KeepsItsMemoAtModuleScope()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Nesting;

            public class Outer
            {
                public class Inner { }
            }

            public static class Use
            {
                public static System.Type Of() => typeof(Outer.Inner);
            }
            """
        );

        var outer = files["nesting/outer.ts"];

        await Assert
            .That(outer)
            .StartsWith("import type { ClassType } from \"metano-runtime\";\n\nlet Outer$Inner$rt");

        await Assert.That(outer).Contains("fullName: \"Nesting.Outer+Inner\",");
        await Assert.That(files["nesting/use.ts"]).Contains("return Outer.Inner.$runtimeType;");
    }

    [Test]
    public async Task UserStaticNamedLikeTheGetter_ReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitRuntimeType]

            namespace Clash;

            public class Widget
            {
                [Name("$runtimeType")]
                public static int Descriptor => 1;
            }
            """
        );

        var clash = diagnostics.Single(d => d.Message.Contains("cannot be emitted"));

        await Assert.That(clash.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
        await Assert.That(clash.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    [Test]
    public async Task ImportedBase_DoesNotMakeTheGetterAnOverride()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitRuntimeType]

            namespace Bases;

            [Import("Widget", from: "widgets")]
            public class Widget { }

            public class Gadget : Widget { }
            """
        );

        var gadget = files["bases/gadget.ts"];

        await Assert.That(gadget).Contains("  static get $runtimeType(): ClassType<Gadget> {");
        await Assert.That(gadget).DoesNotContain("override get $runtimeType");
        await Assert.That(gadget).DoesNotContain("base: () =>");
    }

    /// <summary>
    /// The folded unsigned and signed-byte integral types test and cast by
    /// their own range (spec 011 FR-022): <c>uint</c>, <c>ushort</c> and
    /// <c>sbyte</c> keep their C# type although the IR folds them.
    /// </summary>
    [Test]
    public async Task FoldedIntegralTypes_TestAndCastTheirOwnRange()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Ranges;

            public static class Checks
            {
                public static uint ToUInt(object o) => (uint)o;

                public static ushort ToUShort(object o) => (ushort)o;

                public static sbyte ToSByte(object o) => (sbyte)o;

                public static bool IsUInt(object o) => o is uint;

                public static bool IsUShort(object o) => o is ushort;

                public static bool IsSByte(object o) => o is sbyte;
            }
            """
        );

        var checks = files["ranges/checks.ts"];

        await Assert.That(checks).Contains("castTo<number>(o, UInt32Type)");
        await Assert.That(checks).Contains("castTo<number>(o, UInt16Type)");
        await Assert.That(checks).Contains("castTo<number>(o, SByteType)");
        await Assert.That(checks).Contains("return isUInt32(o);");
        await Assert.That(checks).Contains("return isUInt16(o);");
        await Assert.That(checks).Contains("return isSByte(o);");
    }

    /// <summary>
    /// A numeric enum test is the range of its underlying type wherever it is
    /// rendered, inline, with no descriptor read (spec 011 FR-049 as decided):
    /// a pattern, a dispatcher and the descriptor's own <c>test</c>.
    /// </summary>
    [Test]
    public async Task NumericEnum_TestsTheUnderlyingRangeInline()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace Levels;

            public enum Level : byte { Low, High }

            public static class Use
            {
                public static bool IsLevel(object o) => o is Level;

                public static Level ToLevel(object o) => (Level)o;

                public static string Of(Level level) => "level";

                public static string Of(string text) => text;
            }
            """
        );

        var use = files["levels/use.ts"];

        // On an `object` a Level exists only as a box (spec 011 FR-050).
        await Assert.That(use).Contains("return isBoxOf(o, Level$runtimeType);");
        await Assert.That(use).Contains("isByte(args[0])");
        await Assert.That(use).Contains("castTo<Level>(o, Level$runtimeType)");

        await Assert
            .That(files["levels/level.ts"])
            .Contains("test: (v: unknown): v is Level => isByte(v),");
    }

    /// <summary>A multi-file transpile as one golden: every file, in path order.</summary>
    internal static string TranspileAll(string source) =>
        Concatenated(TranspileHelper.Transpile(source));

    private static string Concatenated(IReadOnlyDictionary<string, string> files) =>
        string.Join(
            "\n",
            files
                .OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => $"// {file.Key}\n{file.Value}")
        );
}
