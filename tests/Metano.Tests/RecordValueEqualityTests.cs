namespace Metano.Tests;

/// <summary>
/// Pins the synthesized <c>equals</c> shape for record positional
/// parameters whose lowered TS surface needs structural comparison
/// instead of strict equality (#202).
///
/// <para>
/// Decimal, Guid, and the Temporal-mapped date / time primitives all
/// flow through JS object wrappers (<c>Decimal</c> from
/// <c>decimal.js</c>; the runtime <c>UUID</c>; <c>Temporal.PlainDate</c>
/// and friends) — comparing them with <c>===</c> would silently
/// diverge from C# value-equality semantics. The compiler now emits
/// <c>valueEquals(this.x, other.x)</c> for those fields, paired with
/// a runtime import line.
/// </para>
/// </summary>
public class RecordValueEqualityTests
{
    [Test]
    public async Task DecimalField_LowersToValueEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Money(decimal Amount, string Currency);
            """
        );

        var output = result["money.ts"];
        await Assert
            .That(output)
            .Contains("import { HashCode, valueEquals } from \"metano-runtime\"");
        await Assert.That(output).Contains("valueEquals(this.amount, other.amount)");
        // Plain primitives stay on `===` so the cheap path keeps working.
        await Assert.That(output).Contains("this.currency === other.currency");
    }

    [Test]
    public async Task DateTimeField_LowersToValueEquals()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public record Event(string Name, DateTime When);
            """
        );

        var output = result["event.ts"];
        await Assert
            .That(output)
            .Contains("import { HashCode, valueEquals } from \"metano-runtime\"");
        await Assert.That(output).Contains("valueEquals(this.when, other.when)");
    }

    [Test]
    public async Task NestedRecordField_LowersToValueEquals()
    {
        // A nested transpiled record carries its own `equals` contract
        // — the parent record's synthesized check should delegate via
        // valueEquals so two semantically equal sub-records compare
        // equal even when they are different instances.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Address(string Street, string City);

            [Transpile]
            public record Customer(string Name, Address Home);
            """
        );

        var output = result["customer.ts"];
        await Assert.That(output).Contains("valueEquals(this.home, other.home)");
    }

    [Test]
    public async Task PrimitiveOnlyRecord_KeepsStrictEquality()
    {
        // No object-wrapper field → no valueEquals import + strictly
        // primitive comparisons. Validates the negative case so the
        // import line doesn't leak into records that don't need it.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record PointI(int X, int Y);
            """
        );

        var output = result["point-i.ts"];
        await Assert.That(output).DoesNotContain("valueEquals");
        await Assert.That(output).Contains("this.x === other.x");
        await Assert.That(output).Contains("this.y === other.y");
    }
}
