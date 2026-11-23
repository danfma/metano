namespace Metano.Tests;

/// <summary>
/// The synthesized record <c>equals</c> / <c>hashCode</c> compare each declaring
/// class's own backing field of an overridable auto-property, as C# compares each
/// class's own storage, rather than reading the most derived accessor twice (#384).
/// </summary>
public class RecordEqualityStorageTests
{
    [Test]
    public async Task OverriddenAutoProperty_IsComparedPerDeclaringClass()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record B1 { public virtual int V { get; init; } }

            [Transpile]
            public record D1 : B1 { public override int V { get; init; } }
            """
        );

        var baseField = BackingField(result["b1.ts"], "B1");
        var derivedField = BackingField(result["d1.ts"], "D1");

        await Assert.That(baseField).IsNotEqualTo(derivedField);
        await Assert.That(result["b1.ts"]).Contains($"this.{baseField} === other.{baseField}");
        await Assert.That(result["b1.ts"]).Contains($"hc.add(this.{baseField});");

        await Assert
            .That(result["d1.ts"])
            .Contains(
                $"return super.equals(other) && this.{derivedField} === other.{derivedField};"
            );

        await Assert.That(result["d1.ts"]).Contains($"hc.add(this.{derivedField});");
    }

    private static string BackingField(string output, string className)
    {
        var start = output.IndexOf($"v${className}_", StringComparison.Ordinal);

        return output[start..output.IndexOf(':', start)];
    }
}
