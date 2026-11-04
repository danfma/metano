using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Tests;

/// <summary>
/// Interface bases (FR-018) and <c>[PlainObject]</c> hierarchies (FR-019) in
/// the generated <c>SerializerContext</c> (spec 009, slice S6c, #268).
///
/// The golden's wire behavior was checked against a source-generated
/// System.Text.Json context over the same C# types (.NET SDK 10.0.401;
/// <c>[PlainObject]</c> is a Metano attribute, so STJ sees plain records):
/// <code>
/// IShape (interface)        Sq(2) → {"$type":"sq","Side":2,"Area":4}; Circle(1) → {"$type":"circle","Radius":1,"Area":3}
///                           {"$type":"sq","Side":2} reads Sq; {"Side":2} → NotSupportedException "The JSON payload for
///                           polymorphic interface or abstract type 'IShape' must specify a type discriminator."
///                           unregistered Other(3) → NotSupportedException "Runtime type 'Other' is not supported by
///                           polymorphic type 'IShape'."
/// Holder(Sq(2), [Circle(1), Sq(3)])
///   → {"Main":{"$type":"sq","Side":2,"Area":4},"All":[{"$type":"circle","Radius":1,"Area":3},{"$type":"sq","Side":3,"Area":9}]}
/// Shape ([PlainObject])     Rect(1, 2) → {"$type":"rect","W":1,"H":2}; Point3(1, 2, 3) → {"$type":"point3","Y":2,"Z":3,"X":1};
///                           Point(1) → {"$type":"point","X":1}; {"$type":"rect","W":1,"H":2} reads Rect
/// Animal (concrete, not self-registered)
///                           Animal("a") → {"Name":"a"}; Dog("d", 3) → {"$type":"dog","Bark":3,"Name":"d"};
///                           {"Name":"a"} reads Animal; unregistered Cat → NotSupportedException
/// SAnimal (self-registered) SAnimal("a") → {"$type":"animal","Name":"a"}, reads back as SAnimal;
///                           SDog("d", 3) → {"$type":"dog","Bark":3,"Name":"d"}
/// Mixed (class + [PlainObject] variants)
///                           MP(1) → {"$type":"p","N":1}; MC("x") → {"$type":"c","S":"x"}
/// </code>
/// The STJ messages name the type with its namespace (<c>'P.IShape'</c>); the
/// runtime names it with <c>typeName</c>, the C# simple name, as for class
/// bases. The generated TypeScript of this golden was compiled with
/// <c>tsgo --strict</c>, bundled with <c>bun build</c> and run under Node:
/// every row above round-trips, with plain objects selected by their
/// synthesized guards.
/// </summary>
public class JsonStructuralPolymorphismTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private readonly List<string> _tempDirs = [];

    private const string ContextBoilerplate = """
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    private const string GoldenDeclarations = """
        [Transpile]
        [JsonDerivedType(typeof(Sq), "sq")]
        [JsonDerivedType(typeof(Circle), "circle")]
        public interface IShape
        {
            double Area { get; }
        }

        [Transpile]
        public sealed record Sq(double Side) : IShape
        {
            public double Area => Side * Side;
        }

        [Transpile]
        public sealed record Circle(double Radius) : IShape
        {
            public double Area => 3 * Radius * Radius;
        }

        [Transpile]
        public sealed record Holder(IShape Main, List<IShape> All);

        [Transpile, PlainObject]
        [JsonDerivedType(typeof(Point), "point")]
        [JsonDerivedType(typeof(Point3), "point3")]
        [JsonDerivedType(typeof(Rect), "rect")]
        public abstract record Shape;

        [Transpile, PlainObject]
        public record Point(double X) : Shape;

        [Transpile, PlainObject]
        public sealed record Point3(double X, double Y, double Z) : Point(X);

        [Transpile, PlainObject]
        public sealed record Rect(double W, double H) : Shape;

        [Transpile, PlainObject]
        [JsonDerivedType(typeof(Dog), "dog")]
        public record Animal(string Name);

        [Transpile, PlainObject]
        public sealed record Dog(string Name, int Bark) : Animal(Name);

        [Transpile, PlainObject]
        [JsonDerivedType(typeof(SAnimal), "animal")]
        [JsonDerivedType(typeof(SDog), "dog")]
        public record SAnimal(string Name);

        [Transpile, PlainObject]
        public sealed record SDog(string Name, int Bark) : SAnimal(Name);

        [Transpile]
        [JsonDerivedType(typeof(MP), "p")]
        [JsonDerivedType(typeof(MC), "c")]
        public abstract record Mixed;

        [Transpile, PlainObject]
        public sealed record MP(int N) : Mixed;

        [Transpile]
        public sealed record MC(string S) : Mixed;

        [Transpile]
        [JsonSerializable(typeof(Holder))]
        [JsonSerializable(typeof(Shape))]
        [JsonSerializable(typeof(Animal))]
        [JsonSerializable(typeof(SAnimal))]
        [JsonSerializable(typeof(Mixed))]
        """;

    [Test]
    public async Task InterfaceAndPlainObjectHierarchies_MatchTheGolden()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert
            .That(ts)
            .IsEqualTo(TranspileHelper.ReadExpected("json-structural-polymorphic-context.ts"));
    }

    // FR-019 rule 1: the guard is synthesized in the variant's own file, the
    // predicate only, without [GenerateGuard].
    [Test]
    public async Task PlainObjectVariants_GetASynthesizedGuard()
    {
        var (files, _) = Transpile(GoldenDeclarations);

        await Assert
            .That(files["test-app/point3.ts"])
            .Contains("export function isPoint3(value: unknown): value is Point3 {");

        await Assert
            .That(files["test-app/point3.ts"])
            .Contains(
                "return typeof v.y === \"number\" && typeof v.z === \"number\" && typeof v.x === \"number\";"
            );

        await Assert.That(files["test-app/point3.ts"]).DoesNotContain("assertPoint3");
        await Assert.That(files["test-app/animal.ts"]).Contains("export function isAnimal(");
        await Assert.That(files["test-app/s-animal.ts"]).Contains("export function isSAnimal(");
        await Assert.That(files["test-app/mp.ts"]).Contains("export function isMP(");
        await Assert.That(files["test-app/shape.ts"]).DoesNotContain("function isShape(");
    }

    [Test]
    public async Task GenerateGuardOnAVariant_KeepsItsAssertCompanion()
    {
        var (files, _) = Transpile(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(Point), "point")]
            public abstract record Shape;

            [Transpile, PlainObject, GenerateGuard]
            public sealed record Point(double X) : Shape;

            [Transpile]
            [JsonSerializable(typeof(Shape))]
            """
        );

        await Assert.That(files["test-app/point.ts"]).Contains("export function isPoint(");
        await Assert.That(files["test-app/point.ts"]).Contains("export function assertPoint(");
        await Assert.That(files[ContextPath]).Contains("test: isPoint,");
    }

    // A guard calls the guard of a member's type only when that type has
    // one; otherwise it checks presence (the call would reference a function
    // nothing declares).
    [Test]
    public async Task VariantGuard_ChecksAMemberWithoutAGuardForPresence()
    {
        var (files, _) = Transpile(
            """
            [Transpile, PlainObject]
            public record Vec(double X);

            [Transpile, PlainObject]
            [JsonDerivedType(typeof(Line), "line")]
            [JsonDerivedType(typeof(Dot), "dot")]
            public abstract record Mark;

            [Transpile, PlainObject]
            public sealed record Line(Vec From, Dot To) : Mark;

            [Transpile, PlainObject]
            public sealed record Dot(double X) : Mark;

            [Transpile]
            [JsonSerializable(typeof(Mark))]
            """
        );

        await Assert
            .That(files["test-app/line.ts"])
            .Contains("return v.from != null && isDot(v.to);");

        await Assert
            .That(files["test-app/line.ts"])
            .Contains("import { isDot, type Dot } from \"./dot\";");
    }

    // Point covers Point3, so Point3 is tested first; listed first, that is
    // attribute order, and testOrder is left out.
    [Test]
    public async Task TestOrder_IsOmittedForAttributeOrder()
    {
        var ts = TranspileContext(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(Point3), "point3")]
            [JsonDerivedType(typeof(Point), "point")]
            public abstract record Shape;

            [Transpile, PlainObject]
            public record Point(double X) : Shape;

            [Transpile, PlainObject]
            public sealed record Point3(double X, double Y, double Z) : Point(X);

            [Transpile]
            [JsonSerializable(typeof(Shape))]
            """
        );

        await Assert.That(ts).Contains("test: isPoint3,");
        await Assert.That(ts).DoesNotContain("testOrder");
    }

    // A nested variant's guard lives in its container's companion namespace.
    [Test]
    public async Task NestedPlainObjectVariant_ReferencesItsQualifiedGuard()
    {
        var (files, _) = Transpile(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(Shape.Circle), "circle")]
            public abstract record Shape
            {
                [Transpile, PlainObject]
                public sealed record Circle(double Radius) : Shape;
            }

            [Transpile]
            [JsonSerializable(typeof(Shape))]
            """
        );

        await Assert.That(files[ContextPath]).Contains("test: Shape.isCircle,");
        await Assert.That(files[ContextPath]).Contains("import { Shape } from \"./shape\";");
        await Assert.That(files["test-app/shape.ts"]).Contains("export function isCircle(");
    }

    // FR-018: an interface's properties include the interfaces it extends,
    // in System.Text.Json's topological order over each interface's
    // AllInterfaces (measured on .NET 10 under FallBackToBaseType:
    // ID : IB, IC, IE with IB : IA and IE : IA writes
    // {"D":4,"E":5,"B":2,"A":1,"C":3}; IP0 : IQ0, IRr0 with IQ0 : IS0 and
    // IRr0 : IT0, IS0 writes {"P0":5,"Rr0":4,"Q0":3,"S0":1,"T0":2}).
    [Test]
    [Arguments(
        """
            public interface IA { int A { get; } }
            public interface IB : IA { int B { get; } }
            public interface IC { int C { get; } }
            public interface IE : IA { int E { get; } }
            public interface ID : IB, IC, IE { int D { get; } }
            """,
        "ID",
        "D,E,B,A,C"
    )]
    [Arguments(
        """
            public interface IS0 { int S0 { get; } }
            public interface IT0 { int T0 { get; } }
            public interface IQ0 : IS0 { int Q0 { get; } }
            public interface IRr0 : IT0, IS0 { int Rr0 { get; } }
            public interface IP0 : IQ0, IRr0 { int P0 { get; } }
            """,
        "IP0",
        "P0,Rr0,Q0,S0,T0"
    )]
    public async Task InterfaceBase_ListsInheritedInterfacePropertiesInStjOrder(
        string interfaces,
        string entry,
        string expectedOrder
    )
    {
        var transpiled = string.Join(
            "\n",
            interfaces
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Select(line =>
                    line.StartsWith($"public interface {entry} ", StringComparison.Ordinal)
                        ? $"[Transpile]\n[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]\n[JsonDerivedType(typeof(Reg), \"r\")]\n{line}"
                        : $"[Transpile]\n{line}"
                )
        );

        var names = expectedOrder.Split(',');

        var ts = TranspileContext(
            $$"""
            {{transpiled}}

            [Transpile]
            public sealed record Reg({{string.Join(
                ", ",
                names.Select(name => $"int {name}")
            )}}) : {{entry}};

            [Transpile]
            [JsonSerializable(typeof({{entry}}))]
            """
        );

        var getterStart = ts.IndexOf(
            $"get {char.ToLowerInvariant(entry[0])}{entry[1..]}()",
            StringComparison.Ordinal
        );
        var getter = ts[
            getterStart..ts.IndexOf("polymorphism: {", getterStart, StringComparison.Ordinal)
        ];

        var actualOrder = getter
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("json: ", StringComparison.Ordinal))
            .Select(line => line["json: \"".Length..^"\",".Length]);

        await Assert.That(string.Join(",", actualOrder)).IsEqualTo(expectedOrder);
    }

    // FR-018 + FR-015: a closed generic interface base (measured on .NET 10:
    // IntBox(1) through IBox<int> → {"$type":"i","Value":1}, read back as IntBox).
    [Test]
    public async Task ClosedGenericInterfaceBase_UsesTheConstructedName()
    {
        var ts = TranspileContext(
            """
            [Transpile]
            [JsonDerivedType(typeof(IntBox), "i")]
            public interface IBox<T> { T Value { get; } }

            [Transpile]
            public sealed record IntBox(int Value) : IBox<int>;

            [Transpile]
            [JsonSerializable(typeof(IBox<int>))]
            """
        );

        await Assert.That(ts).Contains("get iBoxInt32(): TypeSpec<IBox<number>>");
        await Assert.That(ts).Contains("this.createSpec<IBox<number>>({");
        await Assert.That(ts).Contains("typeName: \"IBox\",");
        await Assert.That(ts).Contains("spec: () => this.intBox,");
    }

    // The FR-049 constructor checks never treat an interface as constructed.
    [Test]
    public async Task InterfaceBase_HasNoTypeMember()
    {
        var ts = TranspileContext(GoldenDeclarations);

        await Assert.That(ts).DoesNotContain("type: IShape");
        await Assert.That(ts).DoesNotContain("type: Shape,");
        await Assert.That(ts).Contains("type: Sq,");
        await Assert.That(ts).Contains("type: Mixed,");
    }

    // A [JsonIgnore] override hides the base declaration: the payload never
    // carries it, so the factory literal leaves it out.
    [Test]
    public async Task PlainObjectFactory_LeavesOutAMemberHiddenByJsonIgnore()
    {
        var ts = TranspileContext(
            """
            [Transpile, PlainObject]
            [JsonDerivedType(typeof(D5), "d")]
            public abstract record B5
            {
                public virtual string Name { get; init; } = "";

                public int Keep { get; init; }
            }

            [Transpile, PlainObject]
            public sealed record D5(int V) : B5
            {
                [JsonIgnore]
                public override string Name { get; init; } = "";
            }

            [Transpile]
            [JsonSerializable(typeof(B5))]
            """
        );

        await Assert
            .That(ts)
            .Contains(
                "factory: (p: Record<string, unknown>) => ({ keep: p.keep as number, v: p.v as number }),"
            );
    }

    // FR-047: [PlainObject] variants declared in a referenced Metano package
    // import their guards from the package; the library emits them because
    // its own [JsonDerivedType] registers them (DECIDED-23 a).
    [Test]
    public async Task PackageVariants_ImportTheirGuardsFromThePackage()
    {
        var library = """
            using System.Text.Json.Serialization;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("shared")]

            namespace Shared;

            [PlainObject]
            [JsonDerivedType(typeof(Point), "point")]
            public abstract record Shape;

            [PlainObject]
            public sealed record Point(double X) : Shape;
            """;

        var files = TranspileHelper.TranspileWithLibrary(
            library,
            $$"""
            using System.Text.Json.Serialization;
            using Shared;

            namespace App;

            [Transpile]
            [JsonSerializable(typeof(Shape))]
            {{ContextBoilerplate}}
            """
        );

        var libraryFiles = TranspileHelper.Transpile(library);
        var ts = files["app/json-context.ts"];

        await Assert.That(ts).Contains("test: isPoint,");
        await Assert
            .That(ts)
            .Contains("import { isPoint, type Point, type Shape } from \"shared/shared\";");
        await Assert.That(libraryFiles["shared/point.ts"]).Contains("export function isPoint(");
    }

    // The guard of a variant depends on its base's attributes, in another
    // file; the base is in the variant's group closure, so a cached variant
    // file is re-emitted when the base starts registering it.
    [Test]
    public async Task GroupCache_ReemitsAVariantWhenItsBaseStartsRegisteringIt()
    {
        var cacheDir = CreateTempDir();

        var first = TransformWithCache(cacheDir, registersPoint: false);
        var second = TransformWithCache(cacheDir, registersPoint: true);

        await Assert.That(first).DoesNotContain("function isPoint(");
        await Assert.That(second).Contains("export function isPoint(");
    }

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Transforms an <c>IShape</c> hierarchy with the per-group cache in
    /// <paramref name="cacheDir"/>, writes the files there the way the host
    /// would, and returns <c>app/point.ts</c> as written.
    /// </summary>
    private static string TransformWithCache(string cacheDir, bool registersPoint)
    {
        var registration = registersPoint ? "[JsonDerivedType(typeof(Point), \"point\")]" : "";

        var compilation = TranspileHelper.CompileLibrary(
            $$"""
            using System.Text.Json.Serialization;

            namespace App;

            [Transpile]
            [JsonDerivedType(typeof(Line), "line")]
            {{registration}}
            public interface IShape;

            [Transpile, PlainObject]
            public sealed record Line(double Length) : IShape;

            [Transpile, PlainObject]
            public sealed record Point(double X) : IShape;
            """
        );

        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);
        var printer = new Printer();

        var transformer = new TypeTransformer(ir, compilation)
        {
            CacheOutputDir = cacheDir,
            CacheConfigurationFingerprint = "fp",
        };

        foreach (var file in transformer.TransformAll())
        {
            var path = Path.Combine(cacheDir, file.FileName);
            var content = transformer.CachedFileContents.TryGetValue(file.FileName, out var cached)
                ? cached
                : printer.Print(file);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return File.ReadAllText(Path.Combine(cacheDir, "app", "point.ts"));
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-structural-guards-{Guid.NewGuid():N}");

        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        return dir;
    }

    private static string WrapInNamespace(string declarations) =>
        $$"""
            using System.Collections.Generic;
            using System.Text.Json.Serialization;

            namespace TestApp;

            {{declarations}}

            {{ContextBoilerplate}}
            """;

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) Transpile(string declarations) =>
        TranspileHelper.TranspileWithDiagnostics(WrapInNamespace(declarations));

    private static string TranspileContext(string declarations)
    {
        var (files, diagnostics) = Transpile(declarations);
        var jsonDiagnostics = diagnostics.Where(IsJsonDiagnostic).ToList();

        if (jsonDiagnostics.Count > 0)
        {
            var formatted = string.Join("; ", jsonDiagnostics.Select(d => d.Format()));

            throw new InvalidOperationException("Unexpected diagnostics: " + formatted);
        }

        return files[ContextPath];
    }

    private static bool IsJsonDiagnostic(MetanoDiagnostic diagnostic) =>
        diagnostic.Code
            is DiagnosticCodes.InvalidJsonPolymorphism
                or DiagnosticCodes.LossyJsonPolymorphism
                or DiagnosticCodes.InvalidJsonSerializableRoot
                or DiagnosticCodes.JsonTypeInfoNameCollision;
}
