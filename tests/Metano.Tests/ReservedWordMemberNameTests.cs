namespace Metano.Tests;

/// <summary>
/// A data member named like a JS reserved word is escaped whatever the case of its
/// first letter, and a primary-constructor parameter is promoted to the property whose
/// emitted name it shares (#404).
/// </summary>
public class ReservedWordMemberNameTests
{
    [Test]
    public async Task LowercaseReservedParameter_IsPromotedToItsPascalCaseProperty()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Btn(int of)
            {
                public int Of { get; } = of;
            }
            """
        )["btn.ts"];

        await Assert.That(output).Contains("constructor(readonly of_: number) { }");
        await Assert.That(output).DoesNotContain("readonly of:");
    }

    [Test]
    public async Task RecordPositionalReservedWords_AreEscapedEverywhere()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public record R(int of, int @new);
            """
        )["r.ts"];

        await Assert
            .That(output)
            .Contains("constructor(readonly of_: number, readonly new_: number)");

        await Assert.That(output).Contains("this.of_ === other.of_ && this.new_ === other.new_");
    }

    [Test]
    public async Task PlainObjectReservedWord_KeysTheLiteralAsTheInterface()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject]
            public record Opt(int @new);

            [Transpile]
            public static class UseOpt
            {
                public static int Read() => new Opt(3).@new;
                public static Opt Make() => new Opt(4);
            }
            """
        );

        await Assert.That(result["opt.ts"]).Contains("readonly new_: number;");
        await Assert.That(result["use-opt.ts"]).Contains("return { new_: 4 };");
    }
}
