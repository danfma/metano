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

    [Test]
    public async Task EqualityGuard_OverAnyKind_IsMapped()
    {
        var (files, diagnostics) = TranspileGuards(
            """
            public static void Text(string value) => ArgumentOutOfRangeException.ThrowIfEqual(value, "none");

            public static void Rank(Level value) => ArgumentOutOfRangeException.ThrowIfNotEqual(value, Level.High);
            """
        );

        await Assert.That(files["guard.ts"]).Contains("throwIfEqual(value, \"none\", \"value\");");

        await Assert
            .That(files["guard.ts"])
            .Contains("throwIfNotEqual(value, Level.High, \"value\");");

        await Assert.That(Unsupported(diagnostics)).IsEmpty();
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

    private static (
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) TranspileGuards(string members) =>
        TranspileHelper.TranspileWithDiagnostics(
            $$"""
            [Transpile]
            public enum Level { Low, High }

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
