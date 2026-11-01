namespace Metano.Tests;

public class SwitchPatternTranspileTests
{
    // ─── Switch statement ───────────────────────────────────

    [Test]
    public async Task SwitchStatement_ConstantCases()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Mapper
            {
                public static string Map(int code)
                {
                    switch (code)
                    {
                        case 1: return "one";
                        case 2: return "two";
                        default: return "other";
                    }
                }
            }
            """
        );

        var output = result["mapper.ts"];
        await Assert.That(output).Contains("switch (code)");
        await Assert.That(output).Contains("case 1:");
        await Assert.That(output).Contains("case 2:");
        await Assert.That(output).Contains("default:");
    }

    // ─── Switch expression ──────────────────────────────────

    [Test]
    public async Task SwitchExpression_GeneratesTernaryChain()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Grader
            {
                public static string Grade(int score)
                {
                    return score switch
                    {
                        100 => "perfect",
                        _ => "other",
                    };
                }
            }
            """
        );

        var output = result["grader.ts"];
        await Assert.That(output).Contains("score === 100 ? \"perfect\" : \"other\"");
    }

    // ─── Is pattern ─────────────────────────────────────────

    [Test]
    public async Task IsNull_GeneratesLooseEquality()
    {
        // `is null` lowers to `== null` (loose) so the check also matches
        // a JS-side `undefined` — necessary when a TS consumer produces an
        // absent optional property or an uninitialized field. C# does not
        // expose `undefined`, so relaxing the comparison is safe for
        // round-trip semantics and matches Kotlin/JS behavior.
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, NoContainer]
            public static class Checker
            {
                public static bool IsEmpty(string? value)
                {
                    return value is null;
                }
            }
            """
        );

        var output = result["checker.ts"];
        await Assert.That(output).Contains("value == null");
    }

    [Test]
    public async Task IsNotNull_GeneratesNegatedLooseEquality()
    {
        // `is not null` lowers to `!(value == null)` so the negation also
        // excludes `undefined`. Pairs with `IsNull_GeneratesLooseEquality`
        // — both sides of the C# pattern collapse to the same loose
        // comparison on the TS side.
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, NoContainer]
            public static class Checker
            {
                public static bool HasValue(string? value)
                {
                    return value is not null;
                }
            }
            """
        );

        var output = result["checker.ts"];
        await Assert.That(output).Contains("!(value == null)");
    }

    [Test]
    public async Task IsConstant_GeneratesEquality()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Checker
            {
                public static bool IsZero(int x)
                {
                    return x is 0;
                }
            }
            """
        );

        var output = result["checker.ts"];
        await Assert.That(output).Contains("x === 0");
    }

    [Test]
    public async Task IsType_GeneratesInstanceof()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Money(int Cents);

                [Transpile, NoContainer]
                public static class Checker
                {
                    public static bool IsMoney(object value)
                    {
                        return value is Money;
                    }
                }
            }
            """
        );

        var output = result["app/checker.ts"];
        await Assert.That(output).Contains("value instanceof Money");
    }

    [Test]
    public async Task IsRelational_GeneratesComparison()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Validator
            {
                public static bool IsPositive(int x)
                {
                    return x is > 0;
                }
            }
            """
        );

        var output = result["validator.ts"];
        await Assert.That(output).Contains("x > 0");
    }

    [Test]
    public async Task IsCombinedPattern_GeneratesLogicalAnd()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Range
            {
                public static bool InRange(int x)
                {
                    return x is >= 0 and < 100;
                }
            }
            """
        );

        var output = result["range.ts"];
        await Assert.That(output).Contains("x >= 0 && x < 100");
    }

    [Test]
    public async Task IsOrPattern_GeneratesLogicalOr()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class StatusCheck
            {
                public static bool IsSpecial(int x)
                {
                    return x is 0 or 1;
                }
            }
            """
        );

        var output = result["status-check.ts"];
        await Assert.That(output).Contains("x === 0 || x === 1");
    }

    // ─── Switch expression with patterns ────────────────────

    [Test]
    public async Task SwitchExpression_WithRelationalPatterns()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Grader
            {
                public static string Classify(int score)
                {
                    return score switch
                    {
                        >= 90 => "A",
                        >= 80 => "B",
                        _ => "F",
                    };
                }
            }
            """
        );

        var output = result["grader.ts"];
        await Assert.That(output).Contains("score >= 90 ? \"A\"");
        await Assert.That(output).Contains("score >= 80 ? \"B\"");
        await Assert.That(output).Contains("\"F\"");
    }

    // ─── Designators, catch-all and throw arms (#238) ───────

    private const string ShapeHierarchy = """
        namespace Geo
        {
            [Transpile]
            public abstract record Shape(double X);

            [Transpile]
            public sealed record Circle(double X, double Radius) : Shape(X);

            [Transpile]
            public sealed record Square(double Side, double X) : Shape(X);
        }
        """;

    [Test]
    public async Task SwitchExpression_TypePatternDesignators_BindInsideArms()
    {
        // Designators are bound inside each arm, the IIFE parameter is typed
        // by its argument (no annotation), the discard arm runs
        // unconditionally, its throw expression becomes a throw statement,
        // and nameof folds to a literal.
        var result = TranspileHelper.Transpile(
            """
            namespace Media
            {
                [Transpile]
                public abstract record Clip(double Seconds);

                [Transpile]
                public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

                [Transpile]
                public sealed record SilenceClip(double Seconds) : Clip(Seconds);

                [Transpile]
                public static class ClipDescription
                {
                    public static string Of(Clip clip) => clip switch
                    {
                        ImageClip image => $"image {image.Path}",
                        SilenceClip silence => $"silence {silence.Seconds}",
                        _ => throw new System.ArgumentOutOfRangeException(nameof(clip)),
                    };
                }
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("switch-expression-designators.ts");
        await Assert.That(result["media/clip-description.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task SwitchExpression_GuardReadingDesignator_NestsGuardAfterBinding()
    {
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public static class Sizer
                    {
                        public static string Size(Shape s) => s switch
                        {
                            Circle c when c.Radius > 10 || c.Radius < -10 => "big",
                            Circle { Radius: var r } => $"r={r}",
                            var other when other.X > 0 => "positive",
                            _ => "none",
                        };
                    }
                }
                """
        );

        var output = result["geo/sizer.ts"];
        // A failed guard falls through to the next arm.
        await Assert
            .That(output)
            .Contains(
                """
                      if ($s instanceof Circle) {
                        const c = $s;

                        if (c.radius > 10 || c.radius < -10) {
                          return "big";
                        }
                      }
                """
            );
        // `var` sub-patterns add no test and bind their member.
        await Assert
            .That(output)
            .Contains(
                """
                      if ($s instanceof Circle) {
                        const r = $s.radius;
                """
            );
        // A guarded `var` arm scopes its binding in a block.
        await Assert
            .That(output)
            .Contains(
                """
                      {
                        const other = $s;

                        if (other.x > 0) {
                          return "positive";
                        }
                      }
                """
            );
        await Assert.That(output).Contains("return \"none\";");
        await Assert.That(output).DoesNotContain("if (true)");
        await Assert.That(output).DoesNotContain("Non-exhaustive switch expression");
    }

    [Test]
    public async Task SwitchExpression_GuardWithoutDesignator_ParenthesizesLooseGuard()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Ranges
            {
                public static string Of(int n, bool loud) => n switch
                {
                    > 10 when loud || n > 100 => "big",
                    _ => "small",
                };
            }
            """
        );

        await Assert.That(result["ranges.ts"]).Contains("n > 10 && (loud || n > 100)");
    }

    [Test]
    public async Task NameOf_FoldsToStringLiteral()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Names
            {
                public static string Of(int value) => nameof(value) + nameof(Names.Of);
            }
            """
        );

        var output = result["names.ts"];
        await Assert.That(output).Contains("return \"value\" + \"Of\";");
        await Assert.That(output).DoesNotContain("nameof");
    }

    [Test]
    public async Task SwitchExpression_PositionalRecordPattern_BindsPositionalProperties()
    {
        // A record's Deconstruct reads its positional properties, so the
        // elements read members — never `$s[0]`, which a class instance lacks.
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public static class Positional
                    {
                        public static string Of(Shape s) => s switch
                        {
                            Circle(var x, > 1) => $"wide at {x}",
                            Circle(_, var r) => $"r={r}",
                            _ => "other",
                        };

                        public static bool IsWide(Shape s) => s is Circle(_, > 1);
                    }
                }
                """
        );

        var output = result["geo/positional.ts"];
        await Assert
            .That(output)
            .Contains(
                """
                      if ($s instanceof Circle && $s.radius > 1) {
                        const x = $s.x;

                        return `wide at ${x}`;
                      }
                """
            );
        await Assert.That(output).Contains("const r = $s.radius;");
        await Assert.That(output).Contains("return s instanceof Circle && s.radius > 1;");
        await Assert.That(output).DoesNotContain("$s[");
    }

    [Test]
    public async Task SwitchExpression_TuplePattern_ReadsElementsByPosition()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Pairs
            {
                public static int Second((int, int) pair) => pair switch
                {
                    (0, var y) => y,
                    _ => -1,
                };
            }
            """
        );

        var output = result["pairs.ts"];
        await Assert.That(output).Contains("if ($s[0] === 0) {");
        await Assert.That(output).Contains("const y = $s[1];");
    }

    [Test]
    public async Task SwitchExpression_UserWrittenDeconstruct_ReportsAndStillParses()
    {
        // A hand-written Deconstruct may compute its outputs, so even an out
        // parameter named like a property is not lowered to a member read:
        // MS0001 reports it and the element reads `undefined` (still valid
        // syntax) instead of a bare comment.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public class Range
            {
                public int Low { get; }
                public int High { get; }

                public Range(int low, int high) { Low = low; High = high; }

                public void Deconstruct(out int low, out int to) { low = Low; to = High; }
            }

            [Transpile, NoContainer]
            public static class Ranges
            {
                public static int Width(Range range) => range switch
                {
                    (var a, var b) => b - a,
                };
            }
            """
        );

        var output = files["ranges.ts"];
        await Assert
            .That(output)
            .Contains(
                "const a = undefined /* TODO: positional element 0 has no member to read */;"
            );
        await Assert
            .That(
                diagnostics.Any(d => d.Code == "MS0001" && d.Message.Contains("Range.Deconstruct"))
            )
            .IsTrue();
    }

    [Test]
    public async Task SwitchExpression_ReassignedDesignator_IsDeclaredWithLet()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Counter
            {
                public static int Next(int value) => value switch
                {
                    var n when n > 10 => n,
                    var n => n += 1,
                };
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("const n = $s;");
        await Assert.That(output).Contains("let n = $s;");
        await Assert.That(output).Contains("return n += 1;");
    }

    [Test]
    public async Task SwitchExpression_NonStableScrutinee_EvaluatesItOnce()
    {
        // The ternary fast path repeats the scrutinee in every test, so a
        // call goes through the IIFE, which receives it as one argument.
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public static class Maker
                    {
                        public static Shape Make() => new Circle(0, 1);

                        public static int Kind() => Make() switch
                        {
                            Circle => 1,
                            Square => 2,
                            _ => 0,
                        };
                    }
                }
                """
        );

        var output = result["geo/maker.ts"];
        await Assert.That(output).Contains("})(Maker.make());");
        await Assert.That(output.Split("Maker.make()").Length - 1).IsEqualTo(1);
        await Assert.That(output).Contains("if ($s instanceof Circle) {");
        await Assert.That(output).Contains("if ($s instanceof Square) {");
    }

    [Test]
    public async Task IsPattern_UnderPrefixNot_IsParenthesizedAndValueImported()
    {
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public static class Checks
                    {
                        public static bool NotSquare(Shape s) => !(s is Square);
                    }
                }
                """
        );

        var output = result["geo/checks.ts"];
        await Assert.That(output).Contains("return !(s instanceof Square);");
        await Assert.That(output).Contains("import { Square } from \"./square\";");
    }

    [Test]
    public async Task SwitchStatement_CaseBodyReference_IsImported()
    {
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public static class Factory
                    {
                        public static Shape Create(int kind)
                        {
                            switch (kind)
                            {
                                case 1:
                                    return new Square(1, 0);
                                default:
                                    return new Circle(0, 1);
                            }
                        }
                    }
                }
                """
        );

        var output = result["geo/factory.ts"];
        await Assert.That(output).Contains("import { Square } from \"./square\";");
        await Assert.That(output).Contains("import { Circle } from \"./circle\";");
    }

    [Test]
    public async Task SwitchExpression_AwaitInIifeArm_EmitsAwaitedAsyncIife()
    {
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public static class Loader
                    {
                        static System.Threading.Tasks.Task<string> Load(string key) =>
                            System.Threading.Tasks.Task.FromResult(key);

                        public static async System.Threading.Tasks.Task<string> Describe(Shape s) =>
                            s switch
                            {
                                Circle c => await Load($"{c.Radius}"),
                                _ => "other",
                            };
                    }
                }
                """
        );

        var output = result["geo/loader.ts"];
        await Assert.That(output).Contains("return await (async ($s) => {");
        await Assert.That(output).Contains("return await Loader.load(`${c.radius}`);");
    }

    [Test]
    public async Task SwitchExpression_AwaitInTernaryArm_StaysInline()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Codes
            {
                static System.Threading.Tasks.Task<string> Load(string key) =>
                    System.Threading.Tasks.Task.FromResult(key);

                public static async System.Threading.Tasks.Task<string> Run(int code) =>
                    code switch
                    {
                        1 => await Load("one"),
                        _ => "other",
                    };
            }
            """
        );

        var output = result["codes.ts"];
        await Assert.That(output).Contains("code === 1 ? await load(\"one\") : \"other\"");
        await Assert.That(output).DoesNotContain("async (");
    }

    [Test]
    public async Task Patterns_HonorNameOverridesOnMembers()
    {
        // Positional elements, property sub-patterns and `with` keys read the
        // member under its [Name], as the emitted class declares it.
        var result = TranspileHelper.Transpile(
            """
            namespace Geo
            {
                [Transpile]
                public sealed record Ring(double X, [property: Name("r")] double Radius);

                [Transpile]
                public static class Rings
                {
                    public static double Of(Ring ring) => ring switch
                    {
                        (_, var size) when size > 10 => size,
                        { Radius: > 1 } => 1,
                        _ => 0,
                    };

                    public static Ring Grow(Ring ring) => ring with { Radius = 2 };
                }
            }
            """
        );

        var output = result["geo/rings.ts"];
        await Assert.That(output).Contains("const size = $s.r;");
        await Assert.That(output).Contains("$s.r > 1");
        await Assert.That(output).Contains("ring.with({ r: 2 })");
        await Assert.That(output).DoesNotContain(".radius");
    }

    [Test]
    public async Task PropertyPattern_WithoutTypeFilter_TestsForNull()
    {
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public static class Nulls
                    {
                        public static bool Any(Shape? s) => s is { };

                        public static bool Wide(Circle? c) => c is { Radius: > 1 };
                    }
                }
                """
        );

        var output = result["geo/nulls.ts"];
        await Assert.That(output).Contains("return s != null;");
        await Assert.That(output).Contains("return c != null && c.radius > 1;");
    }

    [Test]
    public async Task SwitchExpression_AssignmentAsMemberTarget_IsParenthesized()
    {
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public static class Swapper
                    {
                        static Circle Fallback() => new Circle(0, 1);

                        public static double Radius(Shape s) => s switch
                        {
                            Circle c => (c = Fallback()).Radius,
                            _ => 0,
                        };
                    }
                }
                """
        );

        var output = result["geo/swapper.ts"];
        await Assert.That(output).Contains("let c = $s;");
        await Assert.That(output).Contains("return (c = Swapper.fallback()).radius;");
    }

    [Test]
    public async Task SwitchExpression_PositionalOverJsTuple_ReadsByIndex()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            namespace Geo
            {
                [Transpile, JsTuple]
                public sealed record Pair(int First, string Second);

                [Transpile]
                public static class Pairs
                {
                    public static string Of(Pair pair) => pair switch
                    {
                        (0, var label) => label,
                        _ => "",
                    };
                }
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("switch-positional-js-tuple.ts");
        await Assert.That(result["geo/pairs.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task SwitchExpression_PositionalOverKeyValuePairAndValueTuple_ReadsByIndex()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;

            [Transpile, NoContainer]
            public static class Entries
            {
                public static int Pick(KeyValuePair<string, int> entry) => entry switch
                {
                    ("a", var v) => v,
                    _ => 0,
                };

                public static string Name((string, int) row) => row switch
                {
                    (var name, > 0) => name,
                    _ => "",
                };
            }
            """
        );

        var output = files["entries.ts"];
        await Assert.That(output).Contains("if ($s[0] === \"a\") {");
        await Assert.That(output).Contains("const v = $s[1];");
        await Assert.That(output).Contains("if ($s[1] > 0) {");
        await Assert.That(output).Contains("const name = $s[0];");
        await Assert.That(output).DoesNotContain("TODO");
        await Assert.That(diagnostics.Any(d => d.Code == "MS0001")).IsFalse();
    }

    [Test]
    public async Task SwitchExpression_LooseScrutineeInTernary_IsParenthesized()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Loose
            {
                public static string Coalesced(int? x) => (x ?? 0) switch
                {
                    0 => "zero",
                    _ => "other",
                };

                public static string Either(bool a, bool b) => (a || b) switch
                {
                    true => "yes",
                    _ => "no",
                };

                public static bool IsZero(int? x) => (x ?? 0) is 0;
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("switch-loose-scrutinee.ts");
        await Assert.That(result["loose.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task MemberAccessOnLooseExpression_IsParenthesized()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Slugs
            {
                public static string Slug(string? value) => (value ?? "").Trim();

                public static int Size(string? a, string b) => (a ?? b).Length;

                public static string Pick(bool cond, string a, string b) => (cond ? a : b).Trim();
            }
            """
        );

        var output = result["slugs.ts"];
        await Assert.That(output).Contains("(value ?? \"\").trim()");
        await Assert.That(output).Contains("(a ?? b).length");
        await Assert.That(output).Contains("(cond ? a : b).trim()");
    }

    [Test]
    public async Task PositionalRecordPattern_WithoutTypeFilter_TestsForNull()
    {
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public static class Wide
                    {
                        public static bool Of(Circle? c) => c is (_, > 1);
                    }
                }
                """
        );

        await Assert.That(result["geo/wide.ts"]).Contains("return c != null && c.radius > 1;");
    }

    [Test]
    public async Task Patterns_ReservedWordMember_UsesDeclaredName()
    {
        // `Default` declares `default_` (a constructor parameter cannot be named
        // `default`), so pattern reads and equality must name `default_` too —
        // the member-reference casing (`default`) would read a missing member.
        var result = TranspileHelper.Transpile(
            """
            namespace Geo
            {
                [Transpile]
                public sealed record Toggle(bool Default);

                [Transpile]
                public static class Toggles
                {
                    public static bool IsDefault(Toggle t) => t is { Default: true };
                }
            }
            """
        );

        await Assert
            .That(result["geo/toggle.ts"])
            .Contains("constructor(readonly default_: boolean)");
        await Assert.That(result["geo/toggle.ts"]).Contains("this.default_ === other.default_");
        await Assert.That(result["geo/toggles.ts"]).Contains("t != null && t.default_ === true");
    }

    [Test]
    public async Task WithOverFieldKeywordInitAccessor_ReportsWarning()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public record Slugged
            {
                public string Slug
                {
                    get;
                    init => field = value.Trim().ToLowerInvariant();
                } = "";

                public int Plain { get; init; }
            }

            [Transpile, NoContainer]
            public static class Renamer
            {
                public static Slugged Rename(Slugged s) => s with { Slug = " A ", Plain = 1 };
            }
            """
        );

        var warnings = diagnostics.Where(d => d.Code == "MS0001").ToList();
        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0].Message).Contains("'Slug'");
        await Assert.That(warnings[0].Message).Contains("custom init accessor is not transpiled");
    }

    [Test]
    public async Task WithOverCustomInitAccessor_AssignsThroughTheSetter()
    {
        // A transpiled `init { … }` body is a TS setter, and copyRecordWith assigns
        // through it, so the body runs; the readonly field it writes is declared
        // mutable because a setter cannot assign a readonly field.
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record Slugged
            {
                private readonly string _slug = "";

                public string Slug
                {
                    get => _slug;
                    init => _slug = value.Trim().ToLowerInvariant();
                }
            }
            """
        );

        var output = result["slugged.ts"];
        await Assert.That(output).Contains("private _slug: string = \"\";");
        await Assert.That(output).Contains("set slug(value: string) {");
        await Assert.That(diagnostics.Where(d => d.Code == "MS0001")).IsEmpty();
    }

    [Test]
    public async Task WithOverCustomSetAccessor_DoesNotWarn()
    {
        // A `set { … }` body is emitted as a TS setter, and copyRecordWith
        // assigns through it, so the body runs: nothing to report.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public record Counter
            {
                private int _value;

                public int Value
                {
                    get => _value;
                    set => _value = value < 0 ? 0 : value;
                }
            }

            [Transpile, NoContainer]
            public static class Counters
            {
                public static Counter Reset(Counter c) => c with { Value = -5 };
            }
            """
        );

        await Assert.That(diagnostics.Any(d => d.Code == "MS0001")).IsFalse();
    }

    [Test]
    public async Task BareTypePatterns_LowerToInstanceofTests()
    {
        // A type with no designator parses as a constant pattern naming a
        // type; it must test `instanceof`, never `=== Circle`.
        var result = TranspileHelper.Transpile(
            ShapeHierarchy
                + """

                namespace Geo
                {
                    [Transpile]
                    public sealed record Slot(Shape Head, Shape Tail);

                    [Transpile]
                    public static class Kinds
                    {
                        public static int Of(Shape s) => s switch
                        {
                            Circle c when c.Radius > 10 => 3,
                            Circle => 1,
                            Square or null => 2,
                            _ => 0,
                        };

                        public static bool HeadIsCircle(Slot slot) => slot is { Head: Circle };

                        public static bool StartsRound(Slot slot) => slot is (Circle, _);

                        public static bool IsSquare(Shape s) => s is Square;
                    }
                }
                """
        );

        var output = result["geo/kinds.ts"];
        await Assert.That(output).Contains("if ($s instanceof Circle) {\n        return 1;");
        await Assert.That(output).Contains("if ($s instanceof Square || $s == null) {");
        // `{ Head: Circle }` and `(Circle, _)` both read the head and test it.
        await Assert
            .That(output.Split("return slot != null && slot.head instanceof Circle;").Length - 1)
            .IsEqualTo(2);
        await Assert.That(output).Contains("return s instanceof Square;");
        await Assert.That(output).DoesNotContain("=== Circle");
        await Assert.That(output).DoesNotContain("=== Square");
    }

    [Test]
    public async Task SwitchExpression_PositionalOverSystemTuple_ReadsByIndex()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Tuples
            {
                public static int Second(System.Tuple<string, int> pair) => pair switch
                {
                    ("a", var n) => n,
                    _ => 0,
                };
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("switch-positional-system-tuple.ts");
        await Assert.That(files["tuples.ts"]).IsEqualTo(expected);
        await Assert.That(diagnostics.Any(d => d.Code == "MS0001")).IsFalse();
    }

    [Test]
    public async Task PositionalPattern_OverNullableInput_TestsForNull()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile, NoContainer]
            public static class Nullables
            {
                public static int Tuple((int, int)? pair) => pair switch
                {
                    (0, var y) => y,
                    _ => -1,
                };

                public static int Entry(KeyValuePair<string, int>? entry) => entry switch
                {
                    (_, var v) => v,
                    _ => -1,
                };

                public static int Plain((int, int) pair) => pair switch
                {
                    (0, var y) => y,
                    _ => -1,
                };
            }
            """
        );

        var output = result["nullables.ts"];
        await Assert.That(output).Contains("if ($s != null && $s[0] === 0) {");
        await Assert.That(output).Contains("if ($s != null) {\n      const v = $s[1];");
        await Assert.That(output).Contains("if ($s[0] === 0) {");
    }

    [Test]
    public async Task PositionalRecordMemberBackedByField_ReadsTheField()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public record Pt(int X, int Y)
            {
                public readonly int X = X;
            }

            [Transpile, NoContainer]
            public static class Points
            {
                public static int FirstOf(Pt p) => p switch
                {
                    (var x, _) => x,
                };
            }
            """
        );

        await Assert.That(files["points.ts"]).Contains("const x = $s.x;");
        await Assert.That(diagnostics.Any(d => d.Code == "MS0001")).IsFalse();
    }

    [Test]
    public async Task BareEnumTypeArm_TestsThePrimitiveKind()
    {
        // An enum lowers to a number / string union with no class value, so
        // `instanceof Color` would throw at runtime; the arm tests typeof.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile]
            public enum Color { Red, Green }

            [Transpile, StringEnum]
            public enum Size { Small, Large }

            [Transpile, NoContainer]
            public static class Kinds
            {
                public static string Of(object o) => o switch
                {
                    Color => "color",
                    Size => "size",
                    _ => "other",
                };
            }
            """
        );

        var output = result["kinds.ts"];
        await Assert.That(output).Contains("typeof o === \"number\" ? \"color\"");
        await Assert.That(output).Contains("typeof o === \"string\" ? \"size\"");
        await Assert.That(output).DoesNotContain("instanceof");
    }

    [Test]
    public async Task PositionalRecord_ExactMemberNameWinsOverCaseInsensitiveField()
    {
        // `x` (a private field) only matches `X` ignoring case; the exact
        // property `X` must win.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public record R(int X)
            {
                private int x = 0;

                public int Hidden => x;
            }

            [Transpile, NoContainer]
            public static class Rs
            {
                public static int Of(R r) => r switch
                {
                    R(var v) when v > 0 => v,
                    _ => 0,
                };
            }
            """
        );

        await Assert.That(result["rs.ts"]).Contains("const v = $s.x;");
    }
}
