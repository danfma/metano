namespace Metano.Tests;

/// <summary>
/// "Golden" tests that capture the FULL TypeScript output for realistic scenarios
/// combining multiple features at once: cross-package types, [EmitInFile] grouping,
/// decimal lowering, and auto-dependency tracking. These tests are useful for visual
/// inspection — when an assertion fails, the full diff between expected and actual is
/// shown, making it easy to verify the output is idiomatic.
/// </summary>
public class EndToEndOutputTests
{
    [Test]
    public async Task CrossPackage_WithEmitInFile_ConsumerOutput_Inspection()
    {
        var library = """
            [assembly: System.Reflection.AssemblyVersion("2.1.0.0")]
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@acme/issues")]

            namespace AcmeIssues
            {
                [EmitInFile("issue")]
                public record Issue(string Title, IssueStatus Status, decimal EstimatedCost);

                [EmitInFile("issue")]
                public enum IssueStatus { Open, Closed }
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Tracker
            {
                public AcmeIssues.Issue? Current { get; set; }
                public AcmeIssues.IssueStatus Status { get; set; }
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);
        var actual = result["tracker.ts"];

        // Pin the FULL output so any future regression in the import format,
        // namespace resolution, or property emission shows up as a clear diff.
        // Notable observations:
        //
        // 1. Cross-package import is MERGED into one line sharing the package root
        //    barrel `@acme/issues`. The two names come from different C# types but
        //    live in the same namespace and are re-exported by the root barrel. The
        //    MIXED `type` qualifier appears because
        //    Issue is referenced only as a type (the property's declared type) while
        //    IssueStatus is used as a value (the auto-init `= IssueStatus.Open`); the
        //    per-name type qualifier handles the asymmetry without splitting into two
        //    import statements.
        //
        // 2. The consumer does NOT import Decimal because it doesn't use decimal
        //    directly — only references Issue, which has a decimal field. The field's
        //    type is observable through Issue's API but never named at the syntax
        //    level in this consumer's source.
        //
        // 3. `status: IssueStatus = IssueStatus.Open;` has the auto-init that mirrors
        //    C#'s `default(IssueStatus)` semantics. Without the explicit initializer,
        //    TS would leave the field as `undefined` at runtime, breaking equality
        //    checks. The compiler picks the first enum member (value 0) automatically.
        var expected =
            "import { type Issue, IssueStatus } from \"@acme/issues\";\n" +
            "\n" +
            "export class Tracker {\n" +
            "  current: Issue | null = null;\n" +
            "\n" +
            "  status: IssueStatus = IssueStatus.Open;\n" +
            "\n" +
            "  constructor() { }\n" +
            "}\n";

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task EmitInFile_LocalGrouping_FullOutput()
    {
        // Producer side: how does the co-located issue.ts file look when we transpile
        // the library directly? Pins the full content so we can see exactly what
        // gets emitted for a multi-type file with a decimal field.
        var result = TranspileHelper.Transpile(
            """
            [assembly: EmitPackage("@acme/issues")]

            namespace AcmeIssues
            {
                [Transpile, EmitInFile("issue")]
                public record Issue(string Title, IssueStatus Status, decimal EstimatedCost);

                [Transpile, EmitInFile("issue")]
                public enum IssueStatus { Open, Closed }
            }
            """);

        var keys = string.Join(", ", result.Keys.OrderBy(k => k));
        var actual = result.TryGetValue("issue.ts", out var content)
            ? content
            : $"<no issue.ts; got files: {keys}>";

        // Pin the FULL output of the merged producer-side file so any future
        // regression in the multi-type emission, decimal mapping, or self-import
        // elision shows up as a clear diff.
        var expected =
            "import { HashCode } from \"metano-runtime\";\n" +
            "import { Decimal } from \"decimal.js\";\n" +
            "\n" +
            "export class Issue {\n" +
            "  constructor(readonly title: string, readonly status: IssueStatus, readonly estimatedCost: Decimal) { }\n" +
            "\n" +
            "  equals(other: any): boolean {\n" +
            "    return other instanceof Issue && this.title === other.title && this.status === other.status && this.estimatedCost === other.estimatedCost;\n" +
            "  }\n" +
            "\n" +
            "  hashCode(): number {\n" +
            "    const hc = new HashCode();\n" +
            "    hc.add(this.title);\n" +
            "    hc.add(this.status);\n" +
            "    hc.add(this.estimatedCost);\n" +
            "    return hc.toHashCode();\n" +
            "  }\n" +
            "\n" +
            "  with(overrides?: Partial<Issue>): Issue {\n" +
            "    return new Issue(overrides?.title ?? this.title, overrides?.status ?? this.status, overrides?.estimatedCost ?? this.estimatedCost);\n" +
            "  }\n" +
            "}\n" +
            "\n" +
            "export enum IssueStatus {\n" +
            "  Open = 0,\n" +
            "  Closed = 1,\n" +
            "}\n";

        await Assert.That(actual).IsEqualTo(expected);
    }
}
