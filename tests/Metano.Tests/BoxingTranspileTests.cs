using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Spec 011 FR-050 / FR-051 (stage 011b B3): a C# boxing conversion of an enum
/// with a descriptor to <c>object</c>, <c>ValueType</c> or <c>Enum</c> lowers to
/// <c>box(value, E$runtimeType)</c>; generic instantiations, interface targets,
/// descriptor-less enums and interop-bound slots stay raw; type questions on a
/// value that may hold a box test for one; interop boundaries unbox only when
/// the compilation boxes; <c>[assembly: DisableBoxing]</c> turns it all off and
/// <c>MS0058</c> warns where a type question may meet a raw value.
/// </summary>
public class BoxingTranspileTests
{
    private const string Box = "box(Color.Red, Color$runtimeType)";

    [Test]
    public async Task BoxingEnums_Golden()
    {
        var output = Transpile(
            """
            public static object Boxed() => Color.Red;

            public static string Questions(object o)
            {
                var t = o.GetType();
                var s = o.ToString();
                var eq = o.Equals(Color.Red);
                var c = (Color)o;

                if (o is Color k && k == Color.Green)
                    return "green";

                return o switch
                {
                    Color.Red => "red",
                    Color other => other.ToString(),
                    _ => $"{t.Name} {s} {eq} {c}",
                };
            }

            public static List<Color> Raw() => [Color.Red];

            public static IComparable Interface() => Color.Red;

            public static object Bcl() => DayOfWeek.Monday;

            public static void Hole(object o) => Js.Code($"console.log({o})");
            """
        );

        await TranspileHelper.AssertGolden("boxing-enums.ts", output);
    }

    [Test]
    public async Task BoxingNullableEnum_Golden()
    {
        var output = Transpile(
            """
            public static object Identifier(Color? maybe) => maybe;

            public static object Call() => Maybe();

            public static Color? Maybe() => null;
            """
        );

        await TranspileHelper.AssertGolden("boxing-nullable-enum.ts", output);
    }

    [Test]
    [Arguments("public static object Of() => Take(Color.Red); static object Take(object o) => o;")]
    [Arguments("public static object Of() { return Color.Red; }")]
    [Arguments("public static object[] Of() => [Color.Red];")]
    [Arguments("public static (object, int) Of() => (Color.Red, 1);")]
    [Arguments("public static object Of(bool b) => b ? Color.Red : \"x\";")]
    [Arguments("public static Func<object> Of() => () => Color.Red;")]
    [Arguments("public static ValueType Of() => Color.Red;")]
    [Arguments("public static Enum Of() => Color.Red;")]
    [Arguments("public static object Of() => (object)Color.Red;")]
    public async Task BoxingConversion_LowersToBox(string member)
    {
        await Assert.That(Transpile(member)).Contains(Box);
    }

    [Test]
    public async Task ConditionalOfEnums_BoxesTheWholeConditional()
    {
        await Assert
            .That(Transpile("public static object Of(bool b) => b ? Color.Red : Color.Green;"))
            .Contains("box(b ? Color.Red : Color.Green, Color$runtimeType)");
    }

    [Test]
    [Arguments("public static List<Color> Of() => [Color.Red];")]
    [Arguments("public static IComparable Of() => Color.Red;")]
    [Arguments("public static object Of() => DayOfWeek.Monday;")]
    [Arguments("public static object Of() => 1;")]
    [Arguments("public static object Of<T>(T v) => v!;")]
    public async Task NotABoxingOfADescribedEnumToObject_StaysRaw(string member)
    {
        await Assert.That(Transpile(member)).DoesNotContain("box(");
    }

    [Test]
    public async Task InteropBoundSlots_StayRaw()
    {
        var output = Transpile(
            """
            [Emit("console.log($0)")]
            public static extern void Log(params object[] values);

            public static void Use()
            {
                Log(Color.Red);
                var dto = new Dto(Color.Red);
                var pair = new Pair(Color.Red, 1);
            }
            """,
            """
            [Transpile, PlainObject] public record Dto(object Value);
            [Transpile, JsTuple] public record Pair(object First, int Second);
            """
        );

        await Assert.That(output).DoesNotContain("box(");
    }

    [Test]
    public async Task MappedBclCall_ReadsRawArguments()
    {
        var output = Transpile("public static bool Of(Perms p) => p.HasFlag(Perms.Read);");

        await Assert.That(output).Contains("return (p & Perms.Read) === Perms.Read;");
    }

    [Test]
    public async Task MappedBclCall_UnboxesAnObjectArgumentWhenTheCompilationBoxes()
    {
        var output = Transpile(
            """
            public static void Log(object o) => Console.WriteLine(o);
            public static void LogColor() => Console.WriteLine(Color.Red);
            public static object Boxed() => Color.Red;
            """
        );

        await Assert.That(output).Contains("console.log(unbox(o));");
        await Assert.That(output).Contains("console.log(Color.Red);");
    }

    [Test]
    public async Task EqualsOnAnObject_BoxesTheArgument()
    {
        await Assert
            .That(Transpile("public static bool Of(object o) => o.Equals(Color.Red);"))
            .Contains($"return defaultEquals(o, {Box});");
    }

    [Test]
    public async Task TypeQuestionsOnAnObject_TestForABox()
    {
        var output = Transpile(
            """
            public static bool Is(object o) => o is Color;
            public static bool IsRed(object o) => o is Color.Red;
            public static Color Cast(object o) => (Color)o;
            public static bool OnInterface(IComparable c) => c is Color;
            public static bool OnEnum(Color c) => c is Color.Red;
            """
        );

        await Assert.That(output).Contains("return isBoxOf(o, Color$runtimeType);");

        await Assert
            .That(output)
            .Contains("return isBoxOf(o, Color$runtimeType) && unbox(o) === Color.Red;");

        await Assert.That(output).Contains("return castTo<Color>(o, Color$runtimeType);");
        await Assert.That(output).Contains("return isType(c, Color$runtimeType);");
        await Assert.That(output).Contains("return c === Color.Red;");
    }

    [Test]
    public async Task SwitchStatementOnAnObject_ComparesThroughTheBox()
    {
        var output = Transpile(
            """
            public static string Of(object o)
            {
                switch (o)
                {
                    case Color.Red:
                        return "red";
                    case Perms p:
                        return p.ToString();
                    default:
                        return "?";
                }
            }
            """
        );

        await Assert
            .That(output)
            .Contains("case isBoxOf(o, Color$runtimeType) && unbox(o) === Color.Red:");

        await Assert.That(output).Contains("const p = unbox(o) as Perms;");
    }

    [Test]
    public async Task InteropUnbox_IsEmittedOnlyWhenTheCompilationBoxes()
    {
        const string hole = "public static void Hole(object o) => Js.Code($\"console.log({o})\");";

        await Assert.That(Transpile(hole)).Contains("console.log(o);");

        await Assert
            .That(Transpile(hole + " public static object Boxed() => Color.Red;"))
            .Contains("console.log(unbox(o));");
    }

    [Test]
    public async Task Concatenation_AppendsTheTextOfABoxOnlyWhenTheCompilationBoxes()
    {
        const string concat = "public static string Text(object o) => \"a\" + o;";

        await Assert.That(Transpile(concat)).Contains("return \"a\" + o;");

        await Assert
            .That(Transpile(concat + " public static object Boxed() => Color.Red;"))
            .Contains("return \"a\" + String(o);");
    }

    [Test]
    public async Task EscapeHatchReceivers_CreateNoBox()
    {
        var output = Transpile(
            """
            public static object Unsafe() => Color.Red.UnsafeCast<object>();
            public static dynamic Dynamic() => Color.Red.AsDynamic();
            public static object Boxed() => Color.Red;
            """
        );

        await Assert.That(output).Contains("return Color.Red as unknown as Object;");
        await Assert.That(output).Contains("return (Color.Red as any);");
    }

    [Test]
    public async Task DisableBoxing_KeepsValuesRaw()
    {
        var output = Transpile(
            """
            public static object Boxed() => Color.Red;
            public static bool Is(object o) => o is Color c && c == Color.Green;
            public static void Hole(object o) => Js.Code($"console.log({o})");
            """,
            disableBoxing: true
        );

        await Assert.That(output).Contains("return Color.Red;");
        await Assert.That(output).Contains("return isInt32(o) && (c = o, true)");
        await Assert.That(output).Contains("console.log(o);");
        await Assert.That(output).DoesNotContain("box(");
    }

    [Test]
    public async Task Ms0058_Reason1_TypeQuestionWithBoxingDisabled()
    {
        var result = TranspileWithDiagnostics(
            "public static string Of(object o) => o.GetType().Name;",
            disableBoxing: true
        );

        var diagnostic = await AssertSingle(result, "o.GetType()");

        await Assert.That(diagnostic.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(result.Output).Contains("return getRuntimeType(o).name;");
    }

    [Test]
    public async Task Ms0058_IsNotReportedWithBoxingOn()
    {
        var result = TranspileWithDiagnostics(
            "public static string Of(object o) => o.GetType().Name; public static object B() => Color.Red;"
        );

        await Assert
            .That(
                result.Diagnostics.Where(d => d.Code == DiagnosticCodes.TypeQuestionOnUnboxedValue)
            )
            .IsEmpty();
    }

    [Test]
    public async Task Ms0058_Reason2_DescriptorLessEnumReadByATypeQuestion()
    {
        var result = TranspileWithDiagnostics(
            """
            public static bool Of()
            {
                object d = DayOfWeek.Monday;

                return d is DayOfWeek;
            }
            """
        );

        var diagnostic = await AssertSingle(result, "DayOfWeek.Monday");

        await Assert.That(diagnostic.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(result.Output).Contains("const d = DayOfWeek.Monday;");
    }

    [Test]
    public async Task Ms0058_Reason2_InterfaceConversionReadByATypeQuestion()
    {
        var result = TranspileWithDiagnostics(
            "public static Type Of() => ((IComparable)Color.Red).GetType();"
        );

        var diagnostic = await AssertSingle(result, "(IComparable)Color.Red");

        await Assert.That(diagnostic.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(result.Output).Contains("return getRuntimeType(Color.Red);");
    }

    [Test]
    public async Task Ms0058_Reason2_IsNotReportedWithoutATypeQuestion()
    {
        var result = TranspileWithDiagnostics("public static object Of() => DayOfWeek.Monday;");

        await Assert
            .That(
                result.Diagnostics.Where(d => d.Code == DiagnosticCodes.TypeQuestionOnUnboxedValue)
            )
            .IsEmpty();
    }

    [Test]
    public async Task Dart_EmitsNoBox()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            Source(
                "public static object Of() => Color.Red; public static bool Is(object o) => o is Color;"
            )
        );

        await Assert.That(string.Concat(files.Values)).DoesNotContain("box");
    }

    private static async Task<MetanoDiagnostic> AssertSingle(Transpiled result, string site)
    {
        var matching = result
            .Diagnostics.Where(d => d.Code == DiagnosticCodes.TypeQuestionOnUnboxedValue)
            .ToList();

        await Assert.That(matching).HasSingleItem();
        await Assert.That(matching[0].Location).IsNotNull();

        var span = matching[0].Location!.SourceSpan;

        await Assert.That(result.Source.Substring(span.Start, span.Length)).IsEqualTo(site);

        return matching[0];
    }

    private static string Transpile(
        string members,
        string types = "",
        bool disableBoxing = false
    ) => TranspileWithDiagnostics(members, types, disableBoxing).Output;

    private static Transpiled TranspileWithDiagnostics(
        string members,
        string types = "",
        bool disableBoxing = false
    )
    {
        var source = Source(members, types, disableBoxing);
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        // TranspileHelper parses the source behind three `using` lines.
        var parsed =
            "using System;\nusing System.Threading.Tasks;\nusing Metano.Annotations;\n" + source;

        return new Transpiled(parsed, files["palette/probe.ts"], diagnostics);
    }

    private static string Source(string members, string types = "", bool disableBoxing = false) =>
        $$"""
            #nullable enable
            using System;
            using System.Collections.Generic;
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;
            using Metano.Interop;

            {{(disableBoxing ? "[assembly: DisableBoxing]" : "")}}

            namespace Palette;

            [Transpile] public enum Color { Red, Green }

            [Transpile, Flags] public enum Perms { None = 0, Read = 1, Write = 2 }

            {{types}}

            [Transpile]
            public static class Probe
            {
                {{members}}
            }
            """;

    private sealed record Transpiled(
        string Source,
        string Output,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    );
}
