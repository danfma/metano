namespace Metano.Tests;

/// <summary>
/// A <c>decimal</c> is a runtime <c>Decimal</c> in every type position, not
/// only inside a class emission (#525, #521): module functions, their overload
/// dispatchers, plain-object fields, a branded type over <c>decimal</c> and its
/// companion functions spell it <c>Decimal</c>, so the values those positions
/// hold type-check against their signatures.
/// </summary>
public class DecimalOutsideClassEmissionTests
{
    private const string Source = """
        using Metano.Annotations;

        [Transpile, NoContainer]
        public static class Prices
        {
            public static decimal Half(decimal x) => x / 2m;

            public static string Pick(decimal d) => "dec " + d;

            public static string Pick(int i) => "int " + i;
        }

        [Transpile, PlainObject]
        public record PriceDto(decimal Amount);

        [Transpile, Branded]
        public readonly record struct Money(decimal Value)
        {
            public static decimal Twice(decimal v) => v * 2m;
        }
        """;

    [Test]
    public async Task ModuleFunctions_SpellDecimal()
    {
        var output = TranspileHelper.Transpile(Source)["prices.ts"];

        await Assert.That(output).Contains("export function half(x: Decimal): Decimal {");
        await Assert.That(output).Contains("export function pick(d: Decimal): string;");
        await Assert.That(output).Contains("return pickD(args[0] as Decimal);");
    }

    [Test]
    public async Task PlainObjectField_SpellsDecimal()
    {
        var output = TranspileHelper.Transpile(Source)["price-dto.ts"];

        await Assert.That(output).Contains("readonly amount: Decimal;");
    }

    [Test]
    public async Task BrandedOverDecimal_BrandsTheDecimal()
    {
        var output = TranspileHelper.Transpile(Source)["money.ts"];

        await Assert.That(output).Contains("export type Money = Decimal & {");
        await Assert.That(output).Contains("function Money$twice(v: Decimal): Decimal {");
    }
}
