namespace Metano.Tests;

/// <summary>
/// Spec 011 stage 011b, hand-in B2: a <c>[PlainObject]</c> is a shape
/// (FR-020). <c>is</c>, <c>as</c>, patterns and dispatch branches call its
/// synthesized structural guard, with or without <c>[GenerateGuard]</c>; a
/// cast reads its descriptor; a reached interface lists its plain-object
/// implementers (FR-019); <c>GetType()</c> on a plain object with no derived
/// plain object folds to its descriptor.
/// </summary>
public class RuntimeTypePlainObjectTests
{
    internal const string PlainObjectSource = """
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Wire;

        public interface IHasLabel
        {
            string Label { get; }
        }

        [PlainObject]
        public record Point(double X, double Y);

        [PlainObject]
        public record Tagged(string Label, int Count) : IHasLabel;

        [PlainObject]
        public record Unasked(string Note);

        public static class Probe
        {
            public static bool IsPoint(object o) => o is Point;

            public static Point? AsPoint(object o) => o as Point;

            public static Point ToPoint(object o) => (Point)o;

            public static int Kind(object o) =>
                o switch
                {
                    Point => 1,
                    Tagged t => t.Count,
                    _ => 0,
                };

            public static bool HasLabel(object o) => o is IHasLabel;

            public static string TypeName(Point p) => p.GetType().Name;

            public static Point Make() => new Point(1, 2);

            public static string MadeTypeName() => Make().GetType().Name;

            public static string NewTypeName() => new Point(0, 0).GetType().Name;

            public static string Over(Point p) => "point";

            public static string Over(string s) => s;
        }
        """;

    /// <summary>
    /// No diagnostic, MS0005 included: the interface descriptor imports its plain
    /// implementer's descriptor, and the implementer only type-imports the interface.
    /// </summary>
    [Test]
    public async Task TheSource_ReportsNoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(PlainObjectSource);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task PlainObjects_MatchesTheGolden() =>
        await TranspileHelper.AssertGolden(
            "plain-object-tests.ts",
            RuntimeTypeDescriptorTests.TranspileAll(PlainObjectSource)
        );

    [Test]
    public async Task PlainObjects_TestsCallTheSynthesizedGuard()
    {
        var files = TranspileHelper.Transpile(PlainObjectSource);
        var probe = files["wire/probe.ts"];

        await Assert.That(probe).Contains("return isPoint(o);");
        await Assert.That(probe).Contains("return isPoint(o) ? o : null;");
        await Assert.That(probe).Contains("castTo<Point>(o, Point$runtimeType)");
        await Assert.That(probe).DoesNotContain("instanceof Point");
        await Assert.That(files["wire/point.ts"]).Contains("export function isPoint(");
    }

    [Test]
    public async Task PlainObjects_AReachedOneDeclaresItsShapeDescriptor()
    {
        var files = TranspileHelper.Transpile(PlainObjectSource);

        await Assert
            .That(files["wire/point.ts"])
            .Contains("export const Point$runtimeType: PlainObjectType<Point> = {");

        await Assert.That(files["wire/point.ts"]).Contains("guard: isPoint,");
        await Assert.That(files["wire/point.ts"]).Contains("test: isPoint,");
        await Assert.That(files["wire/point.ts"]).DoesNotContain("$type");
    }

    [Test]
    public async Task PlainObjects_AnUntestedOneHasNoGuardNorDescriptor()
    {
        var files = TranspileHelper.Transpile(PlainObjectSource);

        await Assert.That(files["wire/unasked.ts"]).DoesNotContain("isUnasked");
        await Assert.That(files["wire/unasked.ts"]).DoesNotContain("$runtimeType");
    }

    [Test]
    public async Task PlainObjects_AReachedInterfaceListsItsPlainImplementers()
    {
        var files = TranspileHelper.Transpile(PlainObjectSource);

        await Assert
            .That(files["wire/i-has-label.ts"])
            .Contains("plainImplementers: [\n    () => Tagged$runtimeType,\n  ],");

        await Assert
            .That(files["wire/tagged.ts"])
            .Contains("export const Tagged$runtimeType: PlainObjectType<Tagged> = {");
    }

    [Test]
    public async Task PlainObjects_GetTypeWithNoDerivedPlainObjectFoldsToTheDescriptor()
    {
        var probe = TranspileHelper.Transpile(PlainObjectSource)["wire/probe.ts"];

        // A receiver that may be null still throws NullReferenceException.
        await Assert.That(probe).Contains("return (getRuntimeType(p), Point$runtimeType).name;");

        await Assert
            .That(probe)
            .Contains("return (getRuntimeType(Probe.make()), Point$runtimeType).name;");

        await Assert.That(probe).Contains("return Point$runtimeType.name;");
    }

    /// <summary>
    /// An interface of a referenced library that this compilation's plain
    /// objects implement is augmented at the test site, never registered into
    /// (FR-019): <c>isType(o, withImplementers(IFoo$runtimeType, [Dto$runtimeType]))</c>.
    /// </summary>
    [Test]
    public async Task PlainObjects_ALibraryInterfaceTestPassesTheLocalImplementers()
    {
        var files = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("labels")]

            namespace Labels;

            public interface IHasLabel
            {
                string Label { get; }
            }
            """,
            """
            using Labels;
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            namespace App;

            [PlainObject]
            public record Tagged(string Label, int Count) : IHasLabel;

            public static class Probe
            {
                public static bool HasLabel(object o) => o is IHasLabel;
            }
            """
        );

        await Assert
            .That(files["app/probe.ts"])
            .Contains(
                "return isType(o, withImplementers(IHasLabel$runtimeType, [Tagged$runtimeType]));"
            );

        await Assert
            .That(files["app/tagged.ts"])
            .Contains("export const Tagged$runtimeType: PlainObjectType<Tagged> = {");
    }
}
