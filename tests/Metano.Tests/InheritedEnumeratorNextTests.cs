using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A class that inherits <c>IEnumerator</c> from a base class and declares its own
/// <c>Next</c> overrides the base's synthesized <c>next()</c> iterator step, which is
/// reported like a conflicting <c>Next</c> on the contract's own type (#350).
/// </summary>
public class InheritedEnumeratorNextTests
{
    private const string Source = """
        using System.Collections;
        using System.Collections.Generic;

        [Transpile]
        public class Countdown : IEnumerator<int>
        {
            private int _n = 3;
            public int Current => _n;
            object IEnumerator.Current => Current;
            public bool MoveNext() => --_n >= 0;
            public void Reset() { }
            public void Dispose() { }
        }

        [Transpile]
        public class Stepper : Countdown
        {
            public int Next() => 42;
        }

        [Transpile]
        public class Plain : Countdown
        {
            public int Step() => 1;
        }
        """;

    [Test]
    public async Task NextOverridingAnInheritedStep_IsReported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        var reported = diagnostics
            .Where(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature
                && d.Severity == MetanoDiagnosticSeverity.Warning
                && d.Message.Contains("inherits IEnumerator from 'Countdown'")
            )
            .ToList();

        await Assert.That(reported).HasSingleItem();
        await Assert.That(reported[0].Message).Contains("'Stepper'");
        await Assert.That(reported[0].Location).IsNotNull();
    }
}
