using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A <c>[PlainObject]</c> that implements an enumeration contract lowers to an
/// interface its object literals can satisfy: without <c>Iterable&lt;T&gt;</c> or
/// <c>Iterator&lt;T&gt;</c>, which they cannot carry, and with a warning (#340).
/// </summary>
public class PlainObjectIterationTests
{
    [Test]
    public async Task EnumerablePlainObject_DropsIterableAndIsReported()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections;
            using System.Collections.Generic;

            [Transpile, PlainObject]
            public record Seq(int[] Items) : IEnumerable<int>
            {
                public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)Items).GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """
        );

        await Assert.That(result["seq.ts"]).Contains("export interface Seq {");
        await Assert.That(result["seq.ts"]).DoesNotContain("Iterable<");

        var reported = diagnostics
            .Where(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature
                && d.Message.StartsWith("[PlainObject] 'Seq' implements an enumeration contract")
            )
            .ToList();

        await Assert.That(reported).HasSingleItem();
        await Assert.That(reported[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
    }

    [Test]
    public async Task EnumeratorPlainObjectWithoutNext_DropsIterator()
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections;
            using System.Collections.Generic;

            [Transpile, PlainObject]
            public record Cursor(int Start) : IEnumerator<int>
            {
                public int Current => Start;
                object IEnumerator.Current => Current;
                public bool MoveNext() => false;
                public void Reset() { }
                public void Dispose() { }
            }
            """
        );

        await Assert.That(result["cursor.ts"]).Contains("export interface Cursor {");
        await Assert.That(result["cursor.ts"]).DoesNotContain("Iterator<");

        await Assert
            .That(diagnostics.Count(d => d.Message.StartsWith("[PlainObject] 'Cursor'")))
            .IsEqualTo(1);
    }
}
