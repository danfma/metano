using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The TypeScript union shape of C# 15 <c>closed</c> families (spec 010 FR-C5, #738,
/// decision W8-2): the family name is the union of its cases, each case carries a
/// read-only <c>kind</c> discriminant, and a family that cannot take the shape keeps
/// the abstract-class one with <c>MS0073</c>.
/// </summary>
public class ClosedFamilyUnionTests
{
    private const string ClosedPolyfill = """

        namespace System.Runtime.CompilerServices
        {
            // The closed-hierarchy marker the .NET 11 BCL ships; the test
            // compilation references the .NET 10 one.
            internal sealed class IsClosedTypeAttribute : Attribute;
        }

        """;

    private const string FamilySource = """
        [assembly: TranspileAssembly]

        namespace Fam
        {
            public closed record Command
            {
                public static Command Default() => new Stop();

                public Command Self() => this;

                public string Describe() => Reader.Name(this);

                protected virtual int Rank() => 0;

                public int RankOf(Command other) => other.Rank();
            }

            public sealed record Play(string Clip) : Command;

            [Name("Halt")]
            public sealed record Stop : Command;

            public closed record Seek : Command;

            public sealed record SeekTo(int Position) : Seek;

            public record SeekBy(int Delta) : Seek;

            public sealed record SeekByTwice(int Delta) : SeekBy(Delta);

            public abstract record Custom : Command;

            public sealed record Macro(string Name) : Custom;

            public closed record Tree<T>;

            public sealed record Leaf<T>(T Value) : Tree<T>;

            public sealed record Node<U>(Tree<U> Left, Tree<U> Right) : Tree<U>;

            public sealed record IntLeaf(int Number) : Tree<int>;

            public closed record Take
            {
                public sealed record Fresh : Take;

                public sealed record Reused(int From) : Take;
            }

            public static class Reader
            {
                public static string Name(Command command) => command switch
                {
                    Play play => play.Clip,
                    Stop => "stop",
                    SeekTo to => "to " + to.Position,
                    SeekBy by => "by " + by.Delta,
                    Custom => "custom",
                };

                public static int SeekOf(object value) => value is Seek seek ? Name(seek).Length : -1;

                public static Seek? AsSeek(object value) => value as Seek;

                public static string Statement(object value)
                {
                    switch (value)
                    {
                        case Seek seek:
                            return Name(seek);
                        default:
                            return "";
                    }
                }
            }
        }
        """;

    private static readonly string[] FamilyFiles =
    [
        "fam/command.ts",
        "fam/seek.ts",
        "fam/seek-by.ts",
        "fam/seek-by-twice.ts",
        "fam/halt.ts",
        "fam/custom.ts",
        "fam/macro.ts",
        "fam/tree.ts",
        "fam/int-leaf.ts",
        "fam/take.ts",
        "fam/reader.ts",
    ];

    [Test]
    public async Task ClosedFamily_MatchesTheGolden()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            FamilySource + ClosedPolyfill
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .DoesNotContain(DiagnosticCodes.ClosedFamilyTagSuppressed);

        await TranspileHelper.AssertGolden(
            "closed-family-union.ts",
            string.Join("\n", FamilyFiles.Select(file => $"// {file}\n{files[file]}"))
        );
    }

    [Test]
    public async Task ClosedRoot_IsAUnionAliasWithAValueBinding()
    {
        var command = TranspileHelper.Transpile(FamilySource + ClosedPolyfill)["fam/command.ts"];

        await Assert.That(command).Contains("abstract class Command$Base {");
        await Assert.That(command).DoesNotContain("export abstract class");
        await Assert.That(command).Contains("export type Command = Play | Halt | Seek | Custom;");
        await Assert
            .That(command)
            .Contains("export const Command: typeof Command$Base = Command$Base;");

        await Assert
            .That(command)
            .Contains(
                "abstract readonly kind: \"Play\" | \"Halt\" | \"SeekTo\" | \"SeekBy\" | \"Custom\";"
            );
    }

    [Test]
    public async Task TagOwners_DeclareAPrototypeAccessor()
    {
        var files = TranspileHelper.Transpile(FamilySource + ClosedPolyfill);

        await Assert
            .That(files["fam/halt.ts"])
            .Contains("override get kind(): \"Halt\" {\n    return \"Halt\";\n  }");

        await Assert.That(files["fam/seek-by.ts"]).Contains("override get kind(): \"SeekBy\"");
        await Assert.That(files["fam/seek-by-twice.ts"]).DoesNotContain("kind");
        await Assert.That(files["fam/custom.ts"]).Contains("override get kind(): \"Custom\"");
        await Assert.That(files["fam/macro.ts"]).DoesNotContain("kind");

        await Assert
            .That(files["fam/seek.ts"])
            .Contains("abstract override readonly kind: \"SeekTo\" | \"SeekBy\";");
    }

    [Test]
    public async Task GenericFamily_KeepsAFixedInstantiationCaseConditional()
    {
        var tree = TranspileHelper.Transpile(FamilySource + ClosedPolyfill)["fam/tree.ts"];

        await Assert
            .That(tree)
            .Contains(
                "export type Tree<T> = Leaf<T> | Node<T> | ([T] extends [number] ? IntLeaf : never);"
            );
    }

    [Test]
    public async Task InFamilyCode_AssertsWhereTheRuntimeClassMeetsTheUnion()
    {
        var files = TranspileHelper.Transpile(FamilySource + ClosedPolyfill);
        var command = files["fam/command.ts"];
        var reader = files["fam/reader.ts"];

        await Assert.That(command).Contains("return this as unknown as Command;");
        await Assert.That(command).Contains("Reader.name(this as unknown as Command)");
        await Assert.That(command).Contains("(other as Command$Base).rank()");
        await Assert
            .That(command)
            .Contains("copyRecordWith<Command>(this as unknown as Command, overrides)");
        await Assert.That(reader).Contains("(seek = value as unknown as Seek, true)");
        await Assert
            .That(reader)
            .Contains("value instanceof Seek ? value as unknown as Seek : null");
        await Assert.That(reader).Contains("const seek = value as unknown as Seek;");
        await Assert.That(reader).Contains("$s instanceof SeekTo");
        await Assert.That(reader).DoesNotContain("$s as unknown");
    }

    /// <summary>
    /// A static initializer runs while the runtime class evaluates, before the value
    /// binding is initialized, so the closed type's own name reads the runtime class.
    /// </summary>
    [Test]
    public async Task OwnNameInTheBody_ReadsTheRuntimeClass()
    {
        var shape = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public closed record Shape
            {
                public static int Created = 0;

                public static int Next = Created + 1;

                public static int Peek() => Shape.Next;
            }

            public sealed record Dot : Shape;
            """ + ClosedPolyfill
        )["shape.ts"];

        await Assert.That(shape).Contains("(Shape$Base.created + 1) | 0");
        await Assert.That(shape).Contains("return Shape$Base.next;");
    }

    [Test]
    public async Task NestedCases_MergeTheCompanionNamespaceWithThePair()
    {
        var take = TranspileHelper.Transpile(FamilySource + ClosedPolyfill)["fam/take.ts"];

        await Assert.That(take).Contains("static get Fresh(): typeof Take$Fresh {");
        await Assert.That(take).Contains("export declare namespace Take {");
        await Assert.That(take).Contains("export type Take = Take$Fresh | Take$Reused;");
        await Assert.That(take).Contains("class Take$Fresh extends Take {");
        await Assert.That(take).Contains("override get kind(): \"Fresh\"");
    }

    [Test]
    public async Task CrossPackageFamily_IsConsumedThroughTheSameName()
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

                public static Lib.Signal? Of(object value) => value as Lib.Signal;
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(library + ClosedPolyfill, consumer)[
            "reader.ts"
        ];

        await Assert.That(output).Contains("import { Go, Halt, Signal } from \"closed-lib/lib\";");
        await Assert
            .That(output)
            .Contains("value instanceof Signal ? value as unknown as Signal : null");
    }

    [Test]
    public async Task DartOutput_GainsNoDiscriminant()
    {
        var (files, _) = DartTranspileHelper.Transpile(FamilySource + ClosedPolyfill);

        foreach (var (_, content) in files)
        {
            await Assert.That(content).DoesNotContain("$Base");
            await Assert.That(content).DoesNotContain("kind");
        }
    }

    [Test]
    [Arguments(
        "public closed record Shape { public string Kind => \"x\"; } public sealed record Dot : Shape;",
        "already emits a 'kind' member"
    )]
    [Arguments(
        "public closed record Shape; public sealed record Dot : Shape; public sealed record Line : Shape { public int Kind; }",
        "already emits a 'kind' member"
    )]
    [Arguments(
        "public abstract record Root { public int Kind => 1; } public closed record Shape : Root; public sealed record Dot : Shape;",
        "already emits a 'kind' member"
    )]
    [Arguments(
        "public closed record Shape { public sealed record Dot : Shape; } namespace Other { public sealed record Dot : global::Shape; }",
        "two cases share the same TypeScript name"
    )]
    [Arguments(
        "[PlainObject] public closed record Shape; public sealed record Dot : Shape;",
        "is [NoEmit] or [PlainObject]"
    )]
    [Arguments(
        "public closed record Tree<T>; public sealed record Wrap<A>(A Item) : Tree<System.Collections.Generic.List<A>>;",
        "the set of cases is not complete"
    )]
    [Arguments(
        "public closed record Duo<T, U>; public sealed record Same<A>(A Item) : Duo<A, A>;",
        "cannot be written as a member of the union"
    )]
    [Arguments(
        "public closed record Shape { public sealed record Base : Shape; public sealed record Dot : Shape; }",
        "takes the runtime class name 'Shape$Base'"
    )]
    [Arguments(
        "public closed record Shape; public abstract record Open : Shape; public closed record Inner : Open; public sealed record Dot : Inner;",
        "derives from an open case of another closed family"
    )]
    public async Task SuppressedFamily_KeepsTheClassShapeWithMs0073(
        string declarations,
        string reason
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            "[assembly: TranspileAssembly]\n\n" + declarations + ClosedPolyfill
        );

        var suppressed = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.ClosedFamilyTagSuppressed
        );

        await Assert.That(suppressed.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(suppressed.Message).Contains(reason);
        await Assert.That(suppressed.Location).IsNotNull();

        var suppressedRoot = suppressed.Message.Contains("'Inner'") ? "inner.ts" : null;

        foreach (
            var (_, content) in files.Where(f => suppressedRoot is null || f.Key == suppressedRoot)
        )
        {
            await Assert.That(content).DoesNotContain("$Base =");
            await Assert.That(content).DoesNotContain("override get kind(): \"");
        }
    }

    [Test]
    public async Task DiscriminatorFamily_KeepsTheClassShapeSilently()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [assembly: TranspileAssembly]

            [StringEnum]
            public enum ShapeKind { Dot, Line }

            [GenerateGuard]
            [Discriminator("Tag")]
            public closed record Shape(ShapeKind Tag);

            [GenerateGuard]
            public sealed record Dot() : Shape(ShapeKind.Dot);

            [GenerateGuard]
            public sealed record Line() : Shape(ShapeKind.Line);
            """ + ClosedPolyfill
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .DoesNotContain(DiagnosticCodes.ClosedFamilyTagSuppressed);

        await Assert.That(files["shape.ts"]).Contains("export abstract class Shape {");
        await Assert.That(files["dot.ts"]).DoesNotContain("get kind()");
    }

    [Test]
    public async Task SourceWithoutClosed_IsUnchanged()
    {
        var files = TranspileHelper.Transpile(
            FamilySource.Replace("closed record", "abstract record")
        );

        foreach (var (_, content) in files)
        {
            await Assert.That(content).DoesNotContain("$Base");
            await Assert.That(content).DoesNotContain("get kind()");
            await Assert.That(content).DoesNotContain("as unknown as");
        }
    }
}
