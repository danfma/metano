using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// C# 15 <c>closed</c> hierarchies (spec 010 FR-C1–FR-C3, FR-012a; #304 slice 1). A
/// closed class is implicitly abstract, so it lowers through the abstract path; a switch
/// over it keeps the TypeScript throw tail and gains a throwing Dart default. Until the
/// union slices land, every union declaration is rejected with MS0038. The test
/// compilations reference the .NET 10 BCL, so the .NET 11 markers are polyfilled
/// (decision W7-2 item 5).
/// </summary>
public class ClosedHierarchyTests
{
    private const string ClosedPolyfill = """

        namespace System.Runtime.CompilerServices
        {
            // The closed-hierarchy marker the .NET 11 BCL ships; the test
            // compilation references the .NET 10 one.
            internal sealed class IsClosedTypeAttribute : Attribute;
        }

        """;

    private const string UnionPolyfill = """

        namespace System.Runtime.CompilerServices
        {
            // The union markers the .NET 11 BCL ships.
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
            public sealed class UnionAttribute : Attribute;

            public interface IUnion
            {
                object? Value { get; }
            }
        }

        """;

    /// <summary>
    /// The library union is reached only through <c>Pets.Make()</c>'s signature: no
    /// union name is spelled in the consumer.
    /// </summary>
    private const string ConsumerOfLibraryUnion = """
        [assembly: TranspileAssembly]

        public static class Reader
        {
            public static object? Read() => Pets.Make().Value;
        }
        """;

    private const string HierarchySource = """
        [assembly: TranspileAssembly]

        public KIND record JobStatus;

        public sealed record Queued : JobStatus;

        public sealed record Running(int Percent) : JobStatus;

        public KIND record Finished : JobStatus;

        public sealed record Completed(double Seconds) : Finished;

        public sealed record Failed(string Error) : Finished;

        public KIND class Shape
        {
            public abstract double Area { get; }
        }

        public sealed class Circle(double radius) : Shape
        {
            public override double Area => 3 * radius * radius;
        }

        public sealed class Square(double side) : Shape
        {
            public override double Area => side * side;
        }

        public KIND record Tree<T>;

        public sealed record Leaf<T>(T Value) : Tree<T>;

        public sealed record Branch<T>(Tree<T> Left, Tree<T> Right) : Tree<T>;

        public KIND record ShotSource
        {
            public sealed record Fresh : ShotSource;

            public sealed record Reused(int From) : ShotSource;
        }

        public static class Describer
        {
            public static string Describe(JobStatus status) => status switch
            {
                Queued => "queued",
                Running(var percent) => $"running {percent}",
                Completed done => $"done in {done.Seconds}",
                Failed(var error) => $"failed: {error}",
            };

            public static string Stage(JobStatus status) => status switch
            {
                Queued => "waiting",
                Running => "busy",
                Finished => "over",
            };

            public static string Kind(Shape shape) => shape switch
            {
                Circle => "circle",
                Square => "square",
            };

            public static int Count<T>(Tree<T> tree) => tree switch
            {
                Leaf<T> => 1,
                Branch<T>(var left, var right) => Count(left) + Count(right),
            };

            public static string Source(ShotSource source) => source switch
            {
                ShotSource.Fresh => "fresh",
                ShotSource.Reused reused => $"reused {reused.From}",
            };

            public static string Generic<TStatus>(TStatus status)
                where TStatus : JobStatus => status switch
            {
                Queued => "queued",
                Running => "running",
                Finished => "finished",
            };

            public static bool IsFresh(object value) => value is ShotSource.Fresh;

            public static bool IsOver(JobStatus status) => status is Finished { };

            public static string Statement(Shape shape)
            {
                switch (shape)
                {
                    case Circle:
                        return "round";
                    case Square square when square.Area > 4:
                        return "big square";
                    default:
                        return "other";
                }
            }
        }
        """;

    private static string ClosedSource =>
        HierarchySource.Replace("KIND", "closed") + ClosedPolyfill;

    private static string AbstractSource => HierarchySource.Replace("KIND", "abstract");

    [Test]
    public async Task ClosedHierarchy_MatchesTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(ClosedSource);

        await TranspileHelper.AssertGolden("closed-hierarchy-describer.ts", result["describer.ts"]);

        await TranspileHelper.AssertGolden(
            "closed-hierarchy-shot-source.ts",
            result["shot-source.ts"]
        );
    }

    /// <summary>
    /// The family files take the union shape (spec 010 FR-C5); code that only uses
    /// the family lowers exactly as for the abstract hierarchy.
    /// </summary>
    [Test]
    public async Task ClosedHierarchy_ChangesOnlyTheFamilyDeclarations()
    {
        var closed = TranspileHelper.Transpile(ClosedSource);
        var abstractOutput = TranspileHelper.Transpile(AbstractSource);

        await Assert.That(closed.Keys.Order()).IsEquivalentTo(abstractOutput.Keys.Order());
        await Assert.That(closed["describer.ts"]).IsEqualTo(abstractOutput["describer.ts"]);
        await Assert.That(closed["job-status.ts"]).IsNotEqualTo(abstractOutput["job-status.ts"]);
    }

    [Test]
    public async Task ExhaustiveClosedSwitch_KeepsTheThrowTail()
    {
        var output = TranspileHelper.Transpile(ClosedSource)["describer.ts"];

        await Assert.That(output).Contains("throw new SwitchExpressionException(");
        await Assert.That(output).DoesNotContain("satisfies never");
    }

    [Test]
    public async Task ClosedPolymorphicJsonBase_KeepsTheAbstractContract()
    {
        const string source = """
            using System.Text.Json.Serialization;

            [assembly: TranspileAssembly]

            [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            [JsonDerivedType(typeof(Play), "play")]
            [JsonDerivedType(typeof(Stop), "stop")]
            public KIND record Command;

            public sealed record Play(string Clip) : Command;

            public sealed record Stop : Command;

            public sealed record Bare(int Id);

            public KIND record Untagged;

            public sealed record UntaggedCase(int Id) : Untagged;

            public sealed record Envelope(Command Command, Untagged Other);

            [JsonSerializable(typeof(Envelope))]
            public partial class WireContext : JsonSerializerContext
            {
                public WireContext() : base(new System.Text.Json.JsonSerializerOptions()) { }

                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;

                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """;

        var closed = TranspileHelper.TranspileWithDiagnostics(
            source.Replace("KIND", "closed") + ClosedPolyfill
        );

        var abstractOutput = TranspileHelper.TranspileWithDiagnostics(
            source.Replace("KIND", "abstract")
        );

        // FR-C5 asserts the spec of a union-lowered family to its union type; the
        // contract itself is the abstract one (FR-C3).
        await Assert
            .That(
                System.Text.RegularExpressions.Regex.Replace(
                    closed.Files["wire-context.ts"],
                    @" as TypeSpec<\w+>",
                    ""
                )
            )
            .IsEqualTo(abstractOutput.Files["wire-context.ts"]);

        await Assert.That(closed.Files["wire-context.ts"]).Contains(" as TypeSpec<Command>");

        await Assert
            .That(closed.Diagnostics.Select(d => d.Code))
            .IsEquivalentTo(abstractOutput.Diagnostics.Select(d => d.Code));
    }

    [Test]
    public async Task CrossPackageClosedBase_SwitchesInTheConsumer()
    {
        const string library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("closed-lib")]

            namespace Lib
            {
                public closed record Signal;

                public sealed record Go : Signal;

                public sealed record Halt(string Reason) : Signal;
            }
            """;
        const string consumer = """
            [assembly: TranspileAssembly]

            public static class Reader
            {
                public static string Read(Lib.Signal signal) => signal switch
                {
                    Lib.Go => "go",
                    Lib.Halt halt => halt.Reason,
                };
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library + ClosedPolyfill, consumer);
        var output = result["reader.ts"];

        await Assert
            .That(output)
            .Contains("import { Go, Halt, type Signal } from \"closed-lib/lib\";");

        await Assert.That(output).Contains("instanceof Go");
        await Assert.That(output).Contains("throw new SwitchExpressionException(");
    }

    [Test]
    public async Task DartClosedSwitch_EndsWithAThrowingDefault()
    {
        var (files, _) = DartTranspileHelper.Transpile(ClosedSource);
        var dart = files["describer.dart"];

        await Assert
            .That(dart)
            .Contains(
                "Circle => 'circle', Square => 'square', "
                    + "_ => throw StateError('Non-exhaustive switch expression') }"
            );

        await Assert.That(dart).Contains("Finished => 'finished', _ => throw StateError(");
    }

    [Test]
    public async Task DartAbstractSwitch_GetsNoThrowingDefault()
    {
        var (files, _) = DartTranspileHelper.Transpile(AbstractSource);

        await Assert.That(files["describer.dart"]).DoesNotContain("StateError");
    }

    [Test]
    public async Task DartClosedSwitchWithCatchAll_GetsNoSecondDefault()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public closed record Light;

            public sealed record Red : Light;

            public sealed record Green : Light;

            public static class Signals
            {
                public static string Name(Light light) => light switch
                {
                    Red => "red",
                    _ => "other",
                };
            }
            """ + ClosedPolyfill
        );

        await Assert.That(files["signals.dart"]).DoesNotContain("StateError");
    }

    [Test]
    [Arguments("public union Clip(int, string);", "Clip")]
    [Arguments(
        "public record Image(string Path); public record Silence(double Seconds); public union Media(Image, Silence);",
        "Media"
    )]
    [Arguments("public static class Holder { public union Inner(int, bool); }", "Inner")]
    public async Task UnionDeclaration_IsRejectedWithMs0038(string declaration, string union)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            "[assembly: TranspileAssembly]\n\n" + declaration + UnionPolyfill
        );

        var rejection = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedUnionShape);

        await Assert.That(rejection.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(rejection.Message).Contains($"Union declaration '{union}'");
        await Assert.That(rejection.Location).IsNotNull();
    }

    [Test]
    public async Task HandWrittenUnion_IsRejectedWithMs0038()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            [System.Runtime.CompilerServices.Union]
            public struct IntOrText : System.Runtime.CompilerServices.IUnion
            {
                public IntOrText(int value) => Value = value;

                public IntOrText(string value) => Value = value;

                public object? Value { get; }
            }
            """ + UnionPolyfill
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .Contains(DiagnosticCodes.UnsupportedUnionShape);
    }

    [Test]
    public async Task UnionOutsideTheTranspiledTypes_IsNotReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            public union ServerOnly(int, string);

            [Transpile]
            public record Shared(int Id);
            """ + UnionPolyfill
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .DoesNotContain(DiagnosticCodes.UnsupportedUnionShape);
    }

    [Test]
    public async Task SameAssemblyUnionWithoutTranspile_IsRejectedAtItsUse()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            public union Pet(int, string);

            [Transpile]
            public static class Owner
            {
                public static object? Read(Pet pet) => pet.Value;

                public static Pet Make() => 5;
            }
            """ + UnionPolyfill
        );

        await AssertSingleUseRejection(diagnostics, "Pet");
    }

    [Test]
    public async Task UnionNestedInTranspiledOwner_IsRejectedAtItsUse()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Owner
            {
                public union Inner(int, bool);

                public static Inner Make() => 5;
            }
            """ + UnionPolyfill
        );

        await AssertSingleUseRejection(diagnostics, "Owner.Inner");
    }

    [Test]
    public async Task UnionFromEmitPackageLibrary_IsRejectedAtItsUse()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            public union Pet(int, string);

            public static class Pets
            {
                public static Pet Make() => 5;
            }
            """ + UnionPolyfill,
            ConsumerOfLibraryUnion
        );

        await AssertSingleUseRejection(diagnostics, "Pet");
    }

    [Test]
    public async Task UnionFromPlainReferencedAssembly_IsRejectedAtItsUse()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            """
            public union Pet(int, string);

            public static class Pets
            {
                public static Pet Make() => 5;
            }
            """ + UnionPolyfill,
            ConsumerOfLibraryUnion
        );

        await AssertSingleUseRejection(diagnostics, "Pet");
    }

    [Test]
    public async Task DartUnionUse_IsRejectedWithMs0038()
    {
        var diagnostics = DartTranspileHelper.FrontendDiagnostics(
            """
            public union Pet(int, string);

            [Transpile]
            public static class Owner
            {
                public static object? Read(Pet pet) => pet.Value;
            }
            """ + UnionPolyfill
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .Contains(DiagnosticCodes.UnsupportedUnionShape);
    }

    private static async Task AssertSingleUseRejection(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string union
    )
    {
        var rejection = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedUnionShape);

        await Assert.That(rejection.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(rejection.Message).Contains($"Union type '{union}'");
        await Assert.That(rejection.Location).IsNotNull();
    }

    [Test]
    public async Task DartUnionDeclaration_IsRejectedWithMs0038()
    {
        var diagnostics = DartTranspileHelper.FrontendDiagnostics(
            "[assembly: TranspileAssembly]\n\npublic union Clip(int, string);" + UnionPolyfill
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .Contains(DiagnosticCodes.UnsupportedUnionShape);
    }
}
