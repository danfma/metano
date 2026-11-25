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

    private const string ExternalType = """
        using Metano.Annotations;

        namespace App.D
        {
            [Transpile]
            [Import("External", "ext-lib")]
            public sealed class External
            {
                public int Y;
            }
        }
        """;

    private const string ExternalModule = """
        declare module "ext-lib" {
          export class External {
            y: number;
          }
        }
        """;

    /// <summary>
    /// A global <c>using</c> alias of an <c>[Import]</c> type imports it from its
    /// external module under the alias (#754 round 2).
    /// </summary>
    [Test]
    public async Task GlobalAliasOfAnImportType_ImportsFromItsModule()
    {
        var files = TranspileFiles(
            ("Globals.cs", "global using Ext = App.D.External;"),
            ("External.cs", ExternalType),
            (
                "Use.cs",
                """
                using Metano.Annotations;

                namespace App.U
                {
                    [Transpile]
                    public static class Use
                    {
                        public static int ExtY(Ext e) => e.Y;
                    }
                }
                """
            )
        );

        var output = files["app/u/use.ts"];

        await Assert.That(output).Contains("import type { External as Ext } from \"ext-lib\";");
        await Assert.That(output).Contains("static extY(e: Ext): number {");
        await AssertTypeChecks(files);
    }

    /// <summary>A file-level alias of an <c>[Import]</c> type, used as a value too.</summary>
    [Test]
    public async Task LocalAliasOfAnImportType_ImportsFromItsModule()
    {
        var files = TranspileFiles(
            ("External.cs", ExternalType),
            (
                "Use.cs",
                """
                using Metano.Annotations;
                using Ext = App.D.External;

                namespace App.U
                {
                    [Transpile]
                    public static class Use
                    {
                        public static int ExtY(Ext e) => e.Y;

                        public static Ext Make() => new Ext();
                    }
                }
                """
            )
        );

        var output = files["app/u/use.ts"];

        await Assert.That(output).Contains("import { External as Ext } from \"ext-lib\";");
        await Assert.That(output).Contains("new Ext()");
        await AssertTypeChecks(files);
    }

    /// <summary>
    /// Two files of one <c>[EmitInFile]</c> group that declare one alias name for two
    /// types cannot share the emitted module's binding: MS0070 (#754 round 2).
    /// </summary>
    [Test]
    public async Task OneAliasNameForTwoTypesInOneEmittedFile_ReportsMs0070()
    {
        var directory = Directory.CreateTempSubdirectory("metano-alias-conflict-");

        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "Nodes.cs"), Nodes);

            File.WriteAllText(
                Path.Combine(directory.FullName, "F1.cs"),
                """
                using Metano.Annotations;
                using N = App.A.Node;

                namespace App.F
                {
                    [Transpile, EmitInFile("shared")]
                    public static class First
                    {
                        public static bool IsA(object o) => o is N;
                    }
                }
                """
            );

            File.WriteAllText(
                Path.Combine(directory.FullName, "F2.cs"),
                """
                using Metano.Annotations;
                using N = App.B.Node;

                namespace App.F
                {
                    [Transpile, EmitInFile("shared")]
                    public static class Second
                    {
                        public static bool IsB(object o) => o is N;
                    }
                }
                """
            );

            var (_, diagnostics) = TranspileHelper.TranspileSourceDirectory(directory.FullName);

            var clashes = diagnostics
                .Where(d => d.Code == DiagnosticCodes.ImportedNameClash)
                .ToList();

            await Assert.That(clashes.Count).IsEqualTo(1);
            await Assert.That(clashes[0].Message).Contains("The alias 'N' names both");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task AssertTypeChecks(Dictionary<string, string> files)
    {
        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string> { ["ext-lib.d.ts"] = ExternalModule }
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
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
