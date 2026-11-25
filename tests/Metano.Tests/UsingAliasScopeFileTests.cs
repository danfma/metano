using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A <c>using</c> alias reaches an emitted file from any tree in its scope (#754 round
/// 1): a <c>global using</c> in another file, or an alias in the second file of an
/// <c>[EmitInFile]</c> group. The file prints the alias and imports it by the symbol it
/// names, so a same-named type of another namespace never takes the bare name.
/// </summary>
public class UsingAliasScopeFileTests
{
    private const string Nodes = """
        using Metano.Annotations;

        namespace App.A
        {
            [Transpile]
            public class Node
            {
                public string Who() => "a";
            }
        }

        namespace App.B
        {
            [Transpile]
            public class Node
            {
                public string Who() => "b";
            }
        }

        namespace App.Z
        {
            [Transpile]
            public class Node
            {
                public string Who() => "z";
            }
        }
        """;

    [Test]
    public async Task GlobalUsingAliasInAnotherFile_ImportsItsTarget()
    {
        var files = TranspileFiles(
            ("Globals.cs", "global using GN = App.B.Node;"),
            ("Nodes.cs", Nodes),
            (
                "V2.cs",
                """
                using Metano.Annotations;

                namespace App.V
                {
                    [Transpile]
                    public static class V2
                    {
                        public static bool Is(object o) => o is GN;

                        public static GN Make() => new GN();
                    }
                }
                """
            )
        );

        var output = files["app/v/v2.ts"];

        await Assert.That(output).Contains("import { Node as GN } from \"../b/node.js\";");
        await Assert.That(output).Contains("o instanceof GN");
        await Assert.That(output).Contains("new GN()");
        await Assert.That(output).DoesNotContain("z/node.js");
        await Assert.That(output).DoesNotContain("a/node.js");
    }

    [Test]
    public async Task AliasInTheSecondFileOfAnEmitInFileGroup_ImportsItsTarget()
    {
        var files = TranspileFiles(
            ("Nodes.cs", Nodes),
            (
                "First.cs",
                """
                using Metano.Annotations;

                namespace App.V
                {
                    [Transpile, EmitInFile("shared")]
                    public static class S1
                    {
                        public static int One() => 1;
                    }
                }
                """
            ),
            (
                "Second.cs",
                """
                using Metano.Annotations;
                using ZN = App.B.Node;

                namespace App.V
                {
                    [Transpile, EmitInFile("shared")]
                    public static class S2
                    {
                        public static bool Is(object o) => o is ZN;
                    }
                }
                """
            )
        );

        var output = files["app/v/shared.ts"];

        await Assert.That(output).Contains("import { Node as ZN } from \"../b/node.js\";");
        await Assert.That(output).Contains("o instanceof ZN");
        await Assert.That(output).DoesNotContain("z/node.js");
    }

    /// <summary>
    /// An inferred lambda parameter typed with an aliased type prints the alias, so it
    /// does not clash with a same-named type the file spells (#751 round 1).
    /// </summary>
    [Test]
    public async Task InferredLambdaOverAnAliasedType_DoesNotClash()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App.A
            {
                [Transpile]
                public class Node
                {
                    public int V { get; set; }
                }
            }

            namespace App.G
            {
                [Transpile]
                public class Node<T>
                {
                    public T Value { get; set; } = default!;
                }
            }

            namespace App.Use
            {
                using System.Collections.Generic;
                using System.Linq;
                using ANode = App.A.Node;

                [Transpile]
                public static class Sums
                {
                    public static int N3(List<ANode> xs, App.G.Node<int> g) =>
                        xs.Sum(n => n.V) + g.Value;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.ImportedNameClash))
            .IsEmpty();

        var output = files["app/use/sums.ts"];

        await Assert.That(output).Contains("(n: ANode) => n.v");
        await Assert.That(output).Contains("import type { Node as ANode } from \"../a/node.js\";");
        await Assert.That(output).Contains("import type { Node } from \"../g/node.js\";");
    }

    private static Dictionary<string, string> TranspileFiles(
        params (string Name, string Source)[] sources
    )
    {
        var directory = Directory.CreateTempSubdirectory("metano-alias-scope-");

        try
        {
            foreach (var (name, source) in sources)
                File.WriteAllText(Path.Combine(directory.FullName, name), source);

            var (files, diagnostics) = TranspileHelper.TranspileSourceDirectory(directory.FullName);

            var errors = diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error);

            return errors.Any()
                ? throw new InvalidOperationException(string.Join("\n", errors))
                : files;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
