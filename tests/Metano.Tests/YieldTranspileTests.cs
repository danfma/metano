namespace Metano.Tests;

public class YieldTranspileTests
{
    [Test]
    public async Task ClassMethod_WithYield_EmitsGeneratorMethod()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class SequenceFactory
            {
                public System.Collections.Generic.IEnumerable<int> Values(int x)
                {
                    yield return 1;
                    if (x < 0) yield break;
                    yield return x;
                }
            }
            """
        );

        var output = result["sequence-factory.ts"];
        await Assert.That(output).Contains("*values(x: number): Generator<number>");
        await Assert.That(output).Contains("yield 1;");
        await Assert.That(output).Contains("yield x;");
        await Assert.That(output).Contains("return;");
    }

    [Test]
    public async Task RuntimeHelperUsedOnlyInsideYield_IsImported()
    {
        // #745: the import walker skipped `yield` statements, so a helper
        // referenced only there was emitted without an import (ReferenceError).
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Classifier
            {
                public System.Collections.Generic.IEnumerable<int> Classify(object o, decimal d)
                {
                    yield return o is int ? 1 : 0;
                    yield return (int)(d * 2m);
                }
            }
            """
        );

        var output = result["classifier.ts"];
        await Assert.That(output).Contains("yield isInt32(o) ? 1 : 0;");

        await Assert
            .That(output)
            .Contains("import { isInt32, narrowFromDecimal } from \"metano-runtime\";");

        await Assert
            .That(output)
            .Contains("import { Decimal } from \"metano-runtime/system/decimal\";");
    }

    [Test]
    public async Task JsxYieldedFromIterator_EmitsTsxFile()
    {
        // #745: JSX detection walks `yield` statements like the import walker,
        // so a module whose only JSX is yielded is still emitted as `.tsx`.
        var result = TranspileHelper.TranspileJsx(
            """
            using System.Collections.Generic;
            using Metano.Annotations;
            using Metano.TypeScript.SolidJs;
            using Metano.TypeScript.SolidJs.Web;

            [Transpile]
            public class Rows
            {
                public IEnumerable<JsxElement> Items()
                {
                    yield return new Html.Div { };
                }
            }
            """
        );

        await Assert.That(result.ContainsKey("rows.tsx")).IsTrue();
    }

    [Test]
    public async Task NoContainer_WithYield_EmitsGeneratorFunction()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class SequenceModule
            {
                public static System.Collections.Generic.IEnumerable<string> Names()
                {
                    yield return "alpha";
                    yield return "beta";
                }
            }
            """
        );

        var output = result["sequence-module.ts"];
        await Assert.That(output).Contains("export function* names(): Generator<string>");
        await Assert.That(output).Contains("yield \"alpha\";");
        await Assert.That(output).Contains("yield \"beta\";");
    }

    [Test]
    public async Task IEnumerableWithoutYield_MapsToIterable()
    {
        // Non-iterator methods returning `IEnumerable<T>` lower to
        // `Iterable<T>` — the lazy-sequence semantics match. Only
        // iterator (yield-bearing) methods route through
        // `MapForGeneratorReturn` and emit `Generator<T>`.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class SnapshotProvider
            {
                public System.Collections.Generic.IEnumerable<int> Snapshot() => [1, 2, 3];
            }
            """
        );

        var output = result["snapshot-provider.ts"];
        await Assert.That(output).Contains("snapshot(): Iterable<number>");
        await Assert.That(output).DoesNotContain("*snapshot(");
    }
}
