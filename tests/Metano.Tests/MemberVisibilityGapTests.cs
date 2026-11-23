using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Member visibility gaps left after #445 (#474): a <c>[PlainObject]</c> keeps its
/// non-positional data properties, an internal one tagged <c>@internal</c>; the
/// declarations that expose an internal hoisted nested type are tagged; an indexer,
/// which a JavaScript class or interface cannot declare, is reported (warning) and left
/// out instead of emitted as invalid TypeScript.
/// </summary>
public class MemberVisibilityGapTests
{
    [Test]
    public async Task PlainObject_DeclaresItsNonPositionalDataProperties()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Pi(int A)
            {
                internal int Hidden { get; init; }
                public int Shown { get; set; }
                public required int Req { get; init; }
                private int Priv { get; init; }
                public int Twice => A * 2;
            }
            """
        )["pi.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                export interface Pi {
                  readonly a: number;
                  /** @internal */
                  readonly hidden?: number;
                  shown?: number;
                  readonly req: number;
                }
                """
            );
    }

    [Test]
    public async Task InternalNestedType_TagsItsGetterAndAlias()
    {
        var output = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Outer
            {
                internal class Inner { public int X { get; set; } }
                public class Open { public int Y { get; set; } }
            }
            """
        )["outer.ts"];

        await Assert
            .That(output)
            .Contains("  /** @internal */\n  static get Inner(): typeof Outer$Inner {");

        await Assert
            .That(output)
            .Contains("  /** @internal */\n  export type Inner = Outer$Inner;");

        await Assert.That(output).Contains("  static get Open(): typeof Outer$Open {");
        await Assert.That(output).DoesNotContain("/** @internal */\n  static get Open");
    }

    [Test]
    public async Task Indexer_IsReportedAndNotEmitted()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Idx
            {
                public int this[int i] => i * 2;
                public int Size { get; set; }
            }
            """
        );

        await Assert.That(result["idx.ts"]).DoesNotContain("this[");
        await Assert.That(result["idx.ts"]).Contains("size: number = 0;");

        var reported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(reported).HasSingleItem();
        await Assert.That(reported[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(reported[0].Message).Contains("the indexer of 'Idx' is not emitted");
    }

    [Test]
    public async Task PlainObjectIndexer_IsReportedAndLeftOutOfTheInterface()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, PlainObject]
            public record WithIdx(int A)
            {
                public int this[int i] => A + i;
            }
            """
        );

        await Assert
            .That(result["with-idx.ts"])
            .Contains(
                """
                export interface WithIdx {
                  readonly a: number;
                }
                """
            );

        var reported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(reported).HasSingleItem();
        await Assert.That(reported[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(reported[0].Message).Contains("the indexer of 'WithIdx' is not emitted");
    }
}
