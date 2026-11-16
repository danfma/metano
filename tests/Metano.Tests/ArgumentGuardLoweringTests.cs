using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// How calls to the .NET argument guards and to user methods with a
/// <c>[CallerArgumentExpression]</c> parameter lower (#651): the comparison
/// guards map only for the <c>T</c> the runtime compares as .NET does, and
/// the compiler writes the caller's argument text where C# would.
/// </summary>
public class ArgumentGuardLoweringTests
{
    [Test]
    [Arguments("char", "'z'")]
    [Arguments("bool", "true")]
    [Arguments("DateOnly", "limit")]
    [Arguments("TimeSpan", "limit")]
    [Arguments("decimal", "limit")]
    public async Task OrderedGuard_OverAComparableKind_IsMapped(string type, string other)
    {
        var (files, diagnostics) = TranspileGuards(
            $"public static void Check({type} value, {type} limit) => ArgumentOutOfRangeException.ThrowIfGreaterThan(value, {other});"
        );

        await Assert.That(files["guard.ts"]).Contains("throwIfGreaterThan(value, ");
        await Assert.That(Unsupported(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// .NET orders a <c>string</c> by the current culture and a <c>Guid</c> by
    /// its bytes, which the runtime does not reproduce: the guard keeps no
    /// mapping and reports <c>MS0001</c>.
    /// </summary>
    [Test]
    [Arguments("string")]
    [Arguments("Guid")]
    public async Task OrderedGuard_OverAnotherKind_IsReported(string type)
    {
        var (_, diagnostics) = TranspileGuards(
            $"public static void Check({type} value, {type} limit) => ArgumentOutOfRangeException.ThrowIfLessThan(value, limit);"
        );

        await Assert
            .That(Unsupported(diagnostics))
            .Contains(message =>
                message.Contains("System.ArgumentOutOfRangeException.ThrowIfLessThan")
            );
    }

    /// <summary>
    /// A sign guard maps for the numbers the runtime holds; another
    /// <c>INumberBase&lt;T&gt;</c>, such as <c>Complex</c>, reports <c>MS0001</c>.
    /// </summary>
    [Test]
    public async Task SignGuard_OverAnotherNumberKind_IsReported()
    {
        var (_, diagnostics) = TranspileGuards(
            "public static void Check(System.Numerics.Complex value) => ArgumentOutOfRangeException.ThrowIfNegative(value);"
        );

        await Assert
            .That(Unsupported(diagnostics))
            .Contains(message =>
                message.Contains("System.ArgumentOutOfRangeException.ThrowIfNegative")
            );
    }

    /// <summary>
    /// The equality guards map for the kinds the runtime's <c>valueEquals</c>
    /// compares as <c>EqualityComparer&lt;T&gt;.Default</c> does.
    /// </summary>
    [Test]
    [Arguments("string", "\"none\"")]
    [Arguments("Level", "Level.High")]
    [Arguments("(int, string)", "(1, \"a\")")]
    [Arguments("((int, int), string)", "((1, 2), \"a\")")]
    [Arguments("TimeSpan", "limit")]
    [Arguments("DateOnly", "limit")]
    [Arguments("decimal", "limit")]
    [Arguments("Guid", "limit")]
    [Arguments("Person", "limit")]
    [Arguments("Point", "limit")]
    [Arguments("Plain", "limit")]
    [Arguments("int?", "null")]
    [Arguments("Line", "limit")]
    [Arguments("Node", "limit")]
    public async Task EqualityGuard_OverAValueKind_IsMapped(string type, string other)
    {
        var (files, diagnostics) = TranspileGuards(
            $"public static void Check({type} value, {type} limit) => ArgumentOutOfRangeException.ThrowIfEqual(value, {other});"
        );

        await Assert.That(files["guard.ts"]).Contains("throwIfEqual(value, ");
        await Assert.That(Unsupported(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A class or an array compares by reference on .NET, which the runtime's
    /// value equality cannot tell from a structural match, and a
    /// <c>DateTimeOffset</c> compares instants where its Temporal value also
    /// compares the time zone: the guard keeps no mapping and reports
    /// <c>MS0001</c>. So does a record or struct holding such a member (an
    /// array in a <c>[PlainObject]</c>, a class in a struct or a record), and a
    /// generic record, whose <c>T</c> member's equality its definition does not
    /// fix.
    /// </summary>
    [Test]
    [Arguments("Account")]
    [Arguments("int[]")]
    [Arguments("object")]
    [Arguments("DateTimeOffset")]
    [Arguments("(int, Account)")]
    [Arguments("Box<TimeSpan>")]
    [Arguments("Box<int>")]
    [Arguments("Tagged")]
    [Arguments("Holder")]
    [Arguments("Owner")]
    public async Task EqualityGuard_OverAReferenceKind_IsReported(string type)
    {
        var (_, diagnostics) = TranspileGuards(
            $"public static void Check({type} value, {type} limit) => ArgumentOutOfRangeException.ThrowIfNotEqual(value, limit);"
        );

        await Assert
            .That(Unsupported(diagnostics))
            .Contains(message =>
                message.Contains("System.ArgumentOutOfRangeException.ThrowIfNotEqual")
            );
    }

    /// <summary>
    /// A guard's type argument is not printed, so a <c>[PlainObject]</c> one,
    /// an interface at run time, is not imported as a value (TS1484 under
    /// <c>verbatimModuleSyntax</c>, and a module Node cannot load).
    /// </summary>
    [Test]
    public async Task EqualityGuard_OverAPlainObject_ImportsNoValue()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Transpile, PlainObject]
            public record Plain(string Name, int Age);

            [Transpile]
            public static class Guard
            {
                public static void Check(Plain value, Plain limit) =>
                    ArgumentOutOfRangeException.ThrowIfEqual(value, limit);
            }
            """
        );

        await Assert.That(files["guard.ts"]).Contains("import type { Plain } from \"./plain\";");
    }

    /// <summary>
    /// <c>throwIfNull</c> asserts its argument non-null, so the code after the
    /// guard reads the narrowed value under <c>strict</c>.
    /// </summary>
    [Test]
    public async Task NullGuard_NarrowsTheArgument()
    {
        var (files, _) = TranspileGuards(
            """
            public static int Length(string? text)
            {
                ArgumentNullException.ThrowIfNull(text);

                return text.Length;
            }
            """
        );

        await Assert
            .That(files["guard.ts"])
            .Contains(
                """
                    throwIfNull(text, "text");

                    return text.length;
                """
            );
    }

    [Test]
    public async Task NamedGuardArgument_PassesTheCallerArgumentExpression()
    {
        var (files, _) = TranspileGuards(
            "public static void Check(string? text) => ArgumentNullException.ThrowIfNull(argument: text);"
        );

        await Assert.That(files["guard.ts"]).Contains("throwIfNull(text, \"text\");");
    }

    /// <summary>
    /// An omitted default before the caller-expression parameter passes
    /// <c>undefined</c>, so the callee's own default applies, whatever its
    /// kind (an enum member, a <c>[StringEnum]</c> or renamed member, a
    /// struct, a nullable).
    /// </summary>
    [Test]
    [Arguments("Level level = Level.High")]
    [Arguments("Mode mode = Mode.Slow")]
    [Arguments("Point point = default")]
    [Arguments("int? limit = null")]
    public async Task OmittedDefaultBeforeTheCallerExpression_PassesUndefined(string parameter)
    {
        var output = TranspileCallerExpressionUse(parameter, "Probe.Text(count + 1)");

        await Assert.That(output).Contains("Probe.text(count + 1, undefined, \"count + 1\")");
    }

    /// <summary>
    /// A named argument normalizes the call into parameter order; the skipped
    /// enum default keeps the member's emitted name.
    /// </summary>
    [Test]
    [Arguments("Level level = Level.High", "Level.High")]
    [Arguments("Mode mode = Mode.Slow", "Mode.Slow")]
    [Arguments("Renamed renamed = Renamed.First", "Renamed.first")]
    public async Task NamedCall_FillsTheCallerExpressionAfterTheDefaults(
        string parameter,
        string emittedDefault
    )
    {
        var output = TranspileCallerExpressionUse(parameter, "Probe.Text(value: count)");

        await Assert.That(output).Contains($"Probe.text(count, {emittedDefault}, \"count\")");
    }

    [Test]
    public async Task ConstructorCall_PassesTheCallerArgumentExpression()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Runtime.CompilerServices;

            [Transpile]
            public sealed class Holder
            {
                public string Text { get; }

                public Holder(int value, [CallerArgumentExpression(nameof(value))] string? text = null) =>
                    Text = text ?? "";
            }

            [Transpile]
            public static class Use
            {
                public static Holder Make(int count) => new Holder(count - 1);
            }
            """
        );

        await Assert.That(files["use.ts"]).Contains("return new Holder(count - 1, \"count - 1\");");
    }

    /// <summary>
    /// A caller-expression parameter before a <c>params</c> one is filled when
    /// the call stops before both (<c>M(x)</c>); only filling the
    /// <c>params</c> slot itself is skipped.
    /// </summary>
    [Test]
    public async Task CallerExpressionBeforeParams_IsFilled()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Runtime.CompilerServices;

            [Transpile]
            public static class Probe
            {
                public static string Text(
                    int value,
                    [CallerArgumentExpression(nameof(value))] string? text = null,
                    params int[] rest
                ) => text ?? "";

                public static string Run(int count) => Text(count);
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains("Probe.text(count, \"count\")");
    }

    /// <summary>
    /// A callee that does not apply its own C# declaration's default receives
    /// the default the call site sees, as C# passes it: an <c>[Import]</c>ed
    /// one has no transpiled default, a delegate has none, and an interface
    /// member's implementation may declare another one.
    /// </summary>
    [Test]
    public async Task OmittedDefault_ForACalleeWithoutItsOwnDefault_PassesTheValue()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Runtime.CompilerServices;
            using Metano.Annotations;

            [Import("Lib", from: "lib")]
            public static class Lib
            {
                public static string Text(
                    int value,
                    int scale = 2,
                    [CallerArgumentExpression(nameof(value))] string? text = null
                ) => text ?? "";
            }

            public delegate string Texter(
                int value,
                int scale = 3,
                [CallerArgumentExpression(nameof(value))] string? text = null
            );

            [Transpile]
            public interface ITexter
            {
                string Text(
                    int value,
                    int scale = 4,
                    [CallerArgumentExpression(nameof(value))] string? text = null
                );
            }

            [Transpile]
            public sealed class Texts : ITexter
            {
                public string Text(
                    int value,
                    int scale = 9,
                    [CallerArgumentExpression(nameof(value))] string? text = null
                ) => text ?? "";
            }

            [Transpile]
            public static class Use
            {
                public static string Imported(int count) => Lib.Text(count);

                public static string Delegated(Texter texter, int count) => texter(count);

                public static string Interfaced(ITexter texter, int count) => texter.Text(count);
            }
            """
        );

        var output = files["use.ts"];

        await Assert.That(output).Contains("Lib.text(count, 2, \"count\")");
        await Assert.That(output).Contains("texter(count, 3, \"count\")");
        await Assert.That(output).Contains("texter.text(count, 4, \"count\")");
    }

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) TranspileGuards(string members) =>
        TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using Metano.Annotations;

            [Transpile]
            public enum Level { Low, High }

            [Transpile]
            public record Person(string Name);

            [Transpile]
            public readonly record struct Point(int X, int Y);

            [Transpile, PlainObject]
            public record Plain(string Name);

            [Transpile]
            public class Account { }

            [Transpile]
            public record Box<T>(T Value);

            [Transpile, PlainObject]
            public record Tagged(string Name, int[] Tags);

            [Transpile]
            public struct Holder { public Account Account; }

            [Transpile]
            public record Owner(string Name, Account Account);

            [Transpile]
            public record Line(Point From, Point To, Level Level, TimeSpan Length);

            [Transpile]
            public record Node(int Value, Node? Next);

            [Transpile]
            public static class Guard
            {
                {{members}}
            }
            """
        );

    private static string TranspileCallerExpressionUse(string parameter, string call) =>
        TranspileHelper.Transpile(
            $$"""
            using System.Runtime.CompilerServices;
            using Metano.Annotations;

            [Transpile]
            public enum Level { Low, High }

            [Transpile, StringEnum]
            public enum Mode { Fast, [Name("slow-mode")] Slow }

            [Transpile]
            public enum Renamed { [Name("first")] First, Second }

            [Transpile]
            public readonly record struct Point(int X, int Y);

            [Transpile]
            public static class Probe
            {
                public static string Text(
                    int value,
                    {{parameter}},
                    [CallerArgumentExpression(nameof(value))] string? text = null
                ) => text ?? "";
            }

            [Transpile]
            public static class Use
            {
                public static string Run(int count) => {{call}};
            }
            """
        )["use.ts"];

    private static List<string> Unsupported(IEnumerable<MetanoDiagnostic> diagnostics) =>
        [
            .. diagnostics
                .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
                .Select(d => d.Message),
        ];
}
