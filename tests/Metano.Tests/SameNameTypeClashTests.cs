using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Two project types printed under one name that one file refers to would share a single
/// import and silently bind the wrong class, so MS0070 reports the second reference (#504).
/// </summary>
public class SameNameTypeClashTests
{
    private const string TwoR23 = """
        namespace Na
        {
            [Transpile]
            public class R23
            {
                public string Tag => "a";
            }
        }

        namespace Nb
        {
            [Transpile]
            public class R23
            {
                public string Tag => "b";
            }
        }

        """;

    [Test]
    public async Task TwoTypesOfOneNameInOneFile_ReportTheSecondReference()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            TwoR23
                + """
                [Transpile]
                public class Probe
                {
                    public string Run() => new Na.R23().Tag + new Nb.R23().Tag;
                }
                """
        );

        var clash = diagnostics.Single(d => d.Code == DiagnosticCodes.ImportedNameClash);

        await Assert.That(clash.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(clash.Message).Contains("'R23' names both 'Na.R23' and 'Nb.R23'");

        await Assert
            .That(
                clash
                    .Location!.SourceTree!.GetText()
                    .Lines[clash.Location.GetLineSpan().StartLinePosition.Line]
                    .ToString()
            )
            .Contains("new Nb.R23()");
    }

    [Test]
    public async Task OneOfTheTwoTypes_ReferencedAlone_ReportsNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            TwoR23
                + """
                [Transpile]
                public class Probe
                {
                    public string Run() => new Na.R23().Tag;
                }

                [Transpile]
                public class Other
                {
                    public string Run() => new Nb.R23().Tag;
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();
    }

    [Test]
    public async Task OneRenamedWithName_ReportsNothing()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Na
            {
                [Transpile]
                public class R23 { }
            }

            namespace Nb
            {
                [Transpile, Name("R23b")]
                public class R23 { }
            }

            [Transpile]
            public class Probe
            {
                public object Run() => new object[] { new Na.R23(), new Nb.R23() };
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        await Assert.That(files["probe.ts"]).Contains("new R23b()");
    }

    private const string GenericNodeAndCycNode = """
        namespace App
        {
            [Transpile]
            public abstract record Node<T>;

            [Transpile]
            public sealed record Leaf<T>(T Value) : Node<T>;
        }

        namespace App.Cyc
        {
            [Transpile]
            public abstract class Node
            {
                public abstract int Size();
            }

            [Transpile]
            public sealed class Bush : Node
            {
                public override int Size() => 1;
            }
        }

        """;

    [Test]
    public async Task SameNameDifferentArity_ImportsTheReferencedType()
    {
        // #743: the import resolved `Node` by its simple name, so every file
        // imported whichever `Node` registered last (App.Cyc.Node).
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            GenericNodeAndCycNode
                + """
                namespace App
                {
                    [Transpile]
                    public class User
                    {
                        public static Node<int> Make() => new Leaf<int>(1);

                        public static bool IsNode(object o) => o is Node<int>;
                    }
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        await Assert.That(files["app/user.ts"]).Contains("import { Node } from \"./node.js\";");
        await Assert.That(files["app/leaf.ts"]).Contains("import { Node } from \"./node.js\";");
        await Assert.That(files["app/cyc/bush.ts"]).Contains("import { Node } from \"./node.js\";");
        await Assert.That(files["app/user.ts"]).DoesNotContain("./cyc/node.js");
        await Assert.That(files["app/leaf.ts"]).DoesNotContain("./cyc/node.js");
    }

    [Test]
    public async Task OneOfTheTwoTypes_ReferencedAlone_ImportsItsOwnModule()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            TwoR23
                + """
                [Transpile]
                public class Probe
                {
                    public string Run() => new Na.R23().Tag;
                }

                [Transpile]
                public class Other
                {
                    public string Run() => new Nb.R23().Tag;
                }
                """
        );

        await Assert.That(files["probe.ts"]).Contains("from \"./na/r23.js\";");
        await Assert.That(files["other.ts"]).Contains("from \"./nb/r23.js\";");
    }

    [Test]
    public async Task GenericAndNonGenericOfOneNameInOneFile_ReportTheSecondReference()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            GenericNodeAndCycNode
                + """
                namespace App
                {
                    [Transpile]
                    public class Mixed
                    {
                        public static Node<int> Make() => new Leaf<int>(1);

                        public static Cyc.Node Bush() => new Cyc.Bush();
                    }
                }
                """
        );

        var clash = diagnostics.Single(d => d.Code == DiagnosticCodes.ImportedNameClash);

        await Assert
            .That(clash.Message)
            .Contains("'Node' names both 'App.Node<T>' and 'App.Cyc.Node'");
    }

    private const string RenamedAndTwoNodes = """
        namespace App.Renamed
        {
            [Transpile, Name("Node")]
            public class Tree
            {
                public string Who() => "Tree";
            }
        }

        namespace App.Cyc
        {
            [Transpile]
            public class Node
            {
                public string Who() => "Cyc";
            }
        }

        namespace App.Zed
        {
            [Transpile]
            public class Node
            {
                public string Who() => "Zed";
            }
        }

        """;

    [Test]
    public async Task NameRenamedType_SpelledByItsCSharpName_ImportsItsOwnModule()
    {
        // #743 review M3: a reference to `Tree` (emitted as `Node`) was resolved by
        // the emitted name, so it imported the last registered `Node`.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            RenamedAndTwoNodes
                + """
                [Transpile]
                public static class UsesRenamed
                {
                    public static string Run() => new App.Renamed.Tree().Who();
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        await Assert
            .That(files["uses-renamed.ts"])
            .Contains("import { Node } from \"./app/renamed/node.js\";");
    }

    [Test]
    public async Task UsingAlias_ImportsTheAliasTarget()
    {
        // #743 review M4: `using CNode = App.Cyc.Node;` imported `Node as CNode`
        // from the last registered `Node`.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            RenamedAndTwoNodes
                + """
                namespace Consumer
                {
                    using CNode = App.Cyc.Node;

                    [Transpile]
                    public static class UsesAlias
                    {
                        public static string Run() => new CNode().Who();
                    }
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        await Assert
            .That(files["consumer/uses-alias.ts"])
            .Contains("import { Node as CNode } from \"../app/cyc/node.js\";");
    }

    /// <summary>
    /// #754: a <c>using</c> alias applies to the type it names only. A same-named type
    /// of another namespace the file also names keeps its own name and module, in type
    /// and in value positions.
    /// </summary>
    [Test]
    public async Task UsingAlias_RenamesOnlyTheTypeItNames()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            AliasNodes
                + """
                namespace Consumer
                {
                    using CNode = App.Cyc.Node;

                    [Transpile]
                    public static class UsesAlias
                    {
                        public static int Sizes(CNode cyc, App.Node<int> node) =>
                            cyc.Size() + node.Value + App.Node<int>.Count + CNode.Total;
                    }
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        var output = files["consumer/uses-alias.ts"];

        await Assert.That(output).Contains("import { Node as CNode } from \"../app/cyc/node.js\";");
        await Assert.That(output).Contains("import { Node } from \"../app/node.js\";");

        await Assert
            .That(output)
            .Contains("static sizes(cyc: CNode, node: Node<number>): number {");

        await Assert.That(output).Contains("Node.count + CNode.total");
    }

    /// <summary>
    /// #754: two aliases of two same-named types bind one import each, and an alias
    /// that spells its target's own name renames nothing.
    /// </summary>
    [Test]
    public async Task TwoAliasesOfSameNamedTypes_BindTheirOwnModules()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            AliasNodes
                + """
                namespace Consumer
                {
                    using GN = App.Node<int>;
                    using Node = App.Cyc.Node;

                    [Transpile]
                    public static class TwoAliases
                    {
                        public static int Run() => new GN().Value + new Node().Size();
                    }
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        var output = files["consumer/two-aliases.ts"];

        await Assert.That(output).Contains("import { Node as GN } from \"../app/node.js\";");
        await Assert.That(output).Contains("import { Node } from \"../app/cyc/node.js\";");
        await Assert.That(output).Contains("new GN<number>().value + new Node().size()");
    }

    /// <summary>
    /// #754: an alias declared in one <c>namespace { }</c> block does not reach a type
    /// the same C# file declares in another block.
    /// </summary>
    [Test]
    public async Task UsingAliasOfAnotherNamespaceBlock_DoesNotApply()
    {
        var files = TranspileHelper.Transpile(
            AliasNodes
                + """
                namespace Consumer.First
                {
                    using CNode = App.Cyc.Node;

                    [Transpile]
                    public static class First
                    {
                        public static int Size(CNode cyc) => cyc.Size();
                    }
                }

                namespace Consumer.Second
                {
                    [Transpile]
                    public static class Second
                    {
                        public static int Size(App.Cyc.Node cyc) => cyc.Size();
                    }
                }
                """
        );

        await Assert
            .That(files["consumer/first/first.ts"])
            .Contains("import type { Node as CNode } from \"../../app/cyc/node.js\";");

        await Assert
            .That(files["consumer/second/second.ts"])
            .Contains("import type { Node } from \"../../app/cyc/node.js\";");

        await Assert.That(files["consumer/second/second.ts"]).DoesNotContain("CNode");
    }

    [Test]
    public async Task AliasedSameNamedTypes_TypeCheckStrictly()
    {
        var files = TranspileHelper.Transpile(
            AliasNodes
                + """
                namespace Consumer
                {
                    using CNode = App.Cyc.Node;
                    using GN = App.Node<int>;

                    [Transpile]
                    public static class Mixed
                    {
                        public static int Run(CNode cyc, App.Node<int> node) =>
                            cyc.Size() + node.Value + new GN().Value + CNode.Total;
                    }
                }
                """
        );

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    private const string AliasNodes = """
        namespace App
        {
            [Transpile]
            public class Node<T>
            {
                public T Value { get; set; } = default!;

                public static int Count => 1;
            }
        }

        namespace App.Cyc
        {
            [Transpile]
            public class Node
            {
                public int Size() => 1;

                public static int Total => 2;
            }
        }

        """;

    [Test]
    public async Task StrictUnionGuardModule_ImportsItsOwnVariants()
    {
        // #743 review M2: the guard module had no bindings, so `isShape` imported
        // the variant guard of the unrelated `Shapes.Zed.Circle`.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            namespace Shapes
            {
                [Transpile, StringEnum, EmitInFile("shape")]
                public enum ShapeKind { Circle, Square }

                [Transpile, GenerateGuard, StrictUnionGuard, Discriminator("Kind")]
                public abstract record Shape(ShapeKind Kind);

                [Transpile, GenerateGuard]
                public sealed record Circle(ShapeKind Kind, double Radius) : Shape(Kind);

                [Transpile, GenerateGuard]
                public sealed record Square(ShapeKind Kind, double Side) : Shape(Kind);
            }

            namespace Shapes.Zed
            {
                [Transpile, GenerateGuard]
                public sealed record Circle(string Name);
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        var guard = files["shapes/shape.guard.ts"];

        await Assert.That(guard).DoesNotContain("./zed/circle.js");
        await Assert.That(guard).Contains("from \"./circle.js\";");
    }

    private const string GuardVariantsReferencingTwoNodes = """
        using Metano.Annotations.TypeScript;

        namespace Shapes
        {
            [Transpile, StringEnum, EmitInFile("shape")]
            public enum ShapeKind { Circle, Square }

            [Transpile, GenerateGuard, StrictUnionGuard, Discriminator("Kind")]
            public abstract record Shape(ShapeKind Kind);

            [Transpile, GenerateGuard]
            public sealed record Circle(ShapeKind Kind, double Radius) : Shape(Kind)
            {
                public int F() => new Na.Node(1).V;
            }

            [Transpile, GenerateGuard]
            public sealed record Square(ShapeKind Kind, double Side) : Shape(Kind)
            {
                public string G() => new Nb.Node("x").S;
            }
        }

        namespace Shapes.Zed
        {
            [Transpile, GenerateGuard]
            public sealed record Circle(string Name);
        }

        namespace Na
        {
            [Transpile]
            public sealed record Node(int V);
        }

        namespace Nb
        {
            [Transpile]
            public sealed record Node(string S);
        }

        """;

    [Test]
    public async Task GuardModule_DoesNotReportWhatItsVariantBodiesReference()
    {
        // #743 review B1: the guard module declares the base and every variant, but it
        // never emits what their bodies reference, so two variants that each name a
        // different `Node` are not a clash in `shape.guard`.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            GuardVariantsReferencingTwoNodes
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        await Assert.That(files["shapes/shape.guard.ts"]).Contains("from \"./circle.js\";");
        await Assert.That(files["shapes/shape.guard.ts"]).DoesNotContain("./zed/circle.js");
    }

    [Test]
    public async Task ClashInsideOneVariant_IsReportedOnce()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            GuardVariantsReferencingTwoNodes.Replace(
                "public int F() => new Na.Node(1).V;",
                "public int F() => new Na.Node(1).V + new Nb.Node(\"y\").S.Length;"
            )
        );

        var clash = diagnostics.Single(d => d.Code == DiagnosticCodes.ImportedNameClash);

        await Assert.That(clash.Message).Contains("in 'circle'");
    }
}
