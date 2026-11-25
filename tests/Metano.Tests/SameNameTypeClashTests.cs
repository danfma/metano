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
}
