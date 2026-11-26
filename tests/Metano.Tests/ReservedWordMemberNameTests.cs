namespace Metano.Tests;

/// <summary>
/// A member named like a JS reserved word keeps its 3.0.0 name: a reserved word is a
/// valid property name. Only identifier positions escape it, so a promoted member that
/// cannot be a parameter property is declared as a field and assigned from the escaped
/// parameter, and a parameter promoted to a PascalCase property declares that
/// property's name (#404).
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

        await Assert.That(output).Contains("constructor(of_: number) {");
        await Assert.That(output).DoesNotContain("readonly of:");
    }

    [Test]
    public async Task RecordPositionalReservedWords_KeepTheirNamesAsFields()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public record R(int of, int @new);
            """
        )["r.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                  declare readonly of: number;
                  declare readonly new: number;

                  constructor(of_: number, new_: number) {
                    this.of = of_;
                    this.new = new_;
                  }
                """
            );

        await Assert.That(output).Contains("this.of === other.of && this.new === other.new");
    }

    [Test]
    public async Task ValidReservedMemberNames_AreUnchanged()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public interface IHas { int @default { get; } }

            [Transpile]
            public class Has : IHas { public int @default => 9; }

            [Transpile]
            public class Fld { public int @default = 3; public int of { get; set; } }

            [Transpile, PlainObject]
            public record Opt(int @new, string @default);

            [Transpile]
            public static class UseOpt
            {
                public static Opt Make() => new Opt(4, "d");
            }
            """
        );

        await Assert.That(result["i-has.ts"]).Contains("readonly default: number;");
        await Assert.That(result["has.ts"]).Contains("get default(): number {");
        await Assert.That(result["fld.ts"]).Contains("this.default = 3;");
        await Assert.That(result["fld.ts"]).Contains("this.of = 0;");
        await Assert.That(result["opt.ts"]).Contains("readonly new: number;");
        await Assert.That(result["opt.ts"]).Contains("readonly default: string;");
        await Assert.That(result["use-opt.ts"]).Contains("return { new: 4, default: \"d\" };");
    }
}
