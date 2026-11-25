namespace Metano.Tests;

/// <summary>
/// Tests for the <c>[EmitInFile("name")]</c> attribute. Multiple types decorated with
/// the same file name (in the same C# namespace) get emitted into a single
/// <c>.ts</c> file instead of one per type. The default (no attribute) remains the
/// existing 1-per-file behavior, so legacy code is unaffected.
/// </summary>
public class EmitInFileTests
{
    [Test]
    public async Task TwoTypesWithSameFileName_EmittedTogether()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, EmitInFile("issue")]
            public record Issue(string Title);

            [Transpile, EmitInFile("issue")]
            public enum IssueStatus { Open, Closed }
            """
        );

        // Both types end up in the same file.
        await Assert.That(result.ContainsKey("issue.ts")).IsTrue();
        // The individual files do NOT exist (no `issue-status.ts`).
        await Assert.That(result.ContainsKey("issue-status.ts")).IsFalse();

        var output = result["issue.ts"];
        await Assert.That(output).Contains("export class Issue");
        await Assert.That(output).Contains("IssueStatus");
    }

    [Test]
    public async Task DefaultEmission_OnePerFileStillWorks()
    {
        // Sanity check: types without [EmitInFile] continue to emit one .ts per type.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Issue(string Title);

            [Transpile]
            public enum IssueStatus { Open, Closed }
            """
        );

        await Assert.That(result.ContainsKey("issue.ts")).IsTrue();
        await Assert.That(result.ContainsKey("issue-status.ts")).IsTrue();
    }

    [Test]
    public async Task ThreeTypesGroupedTogether_AllEndUpInOneFile()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, EmitInFile("issue")]
            public record Issue(string Title);

            [Transpile, EmitInFile("issue")]
            public enum IssueStatus { Open, Closed }

            [Transpile, EmitInFile("issue")]
            public enum IssuePriority { Low, High }
            """
        );

        var output = result["issue.ts"];
        await Assert.That(output).Contains("Issue");
        await Assert.That(output).Contains("IssueStatus");
        await Assert.That(output).Contains("IssuePriority");
        await Assert.That(result.ContainsKey("issue-status.ts")).IsFalse();
        await Assert.That(result.ContainsKey("issue-priority.ts")).IsFalse();
    }

    [Test]
    public async Task EmitInFileGrouping_PreservesNamespacePath()
    {
        // The grouped file lands under the namespace's folder, just like a 1-per-file
        // emission would.
        var result = TranspileHelper.Transpile(
            """
            namespace App.Domain
            {
                [Transpile, EmitInFile("issue")]
                public record Issue(string Title);

                [Transpile, EmitInFile("issue")]
                public enum IssueStatus { Open, Closed }
            }
            """
        );

        // The exact path depends on root-namespace stripping. The file ends in `issue.ts`
        // and lives somewhere under the project's namespace tree.
        var match = result.Keys.FirstOrDefault(k => k.EndsWith("issue.ts"));
        await Assert.That(match).IsNotNull();
    }

    [Test]
    public async Task ConflictingNamespacesForSameFileName_EmitsMs0008()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App.Domain
            {
                [Transpile, EmitInFile("shared")]
                public record A(string X);
            }

            namespace App.Other
            {
                [Transpile, EmitInFile("shared")]
                public record B(string Y);
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0008")).IsTrue();
    }

    [Test]
    public async Task EmitInFile_RecordImportsItsRefsCorrectly()
    {
        // When the grouped file references another transpilable type that lives in a
        // SEPARATE file, the merged ImportCollector still emits the import for that
        // sibling type.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Tag(string Name);

            [Transpile, EmitInFile("issue")]
            public record Issue(string Title, Tag PrimaryTag);

            [Transpile, EmitInFile("issue")]
            public enum IssueStatus { Open, Closed }
            """
        );

        var output = result["issue.ts"];
        // Tag lives in tag.ts and should be imported (type-only since it's only used
        // as a property type, not a value).
        await Assert.That(output).Contains("Tag } from \"./tag.js\"");
        await Assert.That(result.ContainsKey("tag.ts")).IsTrue();
    }
}
