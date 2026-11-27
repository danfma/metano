using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Interpolated strings converted to a custom interpolated-string handler
/// (FR-112, #828): the handler construction with Roslyn's
/// <c>literalLength</c> / <c>formattedCount</c> and its
/// <c>[InterpolatedStringHandlerArgument]</c> values, one call per part to the
/// exact <c>Append*</c> overload Roslyn bound, then the handler; a hoisted
/// <c>const</c> where the conversion is the statement's value, a comma
/// expression elsewhere. BCL handlers keep the template literal, and every
/// shape the lowering does not cover reports <c>MS0001</c>.
/// </summary>
public class InterpolatedStringHandlerTests
{
    private const string TemplateSource = """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        [Transpile]
        public sealed class Signal<T>
        {
            public T Value { get; set; } = default!;
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class TextTemplate
        {
            private readonly List<string> _parts = new();

            public TextTemplate(int literalLength, int formattedCount) { }

            public void AppendLiteral(string literal) => _parts.Add(literal);

            public void AppendFormatted<T>(Signal<T> signal) => _parts.Add("signal");

            public void AppendFormatted<T>(T value) => _parts.Add("value");

            public void AppendFormatted<T>(T value, string? format) => _parts.Add("format");

            public void AppendFormatted<T>(T value, int alignment) => _parts.Add("alignment");

            public string Render() => string.Join("", _parts);
        }

        [Transpile]
        public static class Ui
        {
            public static string Bind(TextTemplate template) => template.Render();

            public static string Run(Signal<int> count, int n)
            {
                var a = Bind($"Count: {count} and {n:D3} or {n,5}");
                TextTemplate t = $"x{n}";
                TextTemplate u = $"x{n}" + $"y{count}";
                var c = Bind((TextTemplate)$"c{n}");

                return a + t.Render() + u.Render() + c;
            }
        }
        """;

    private const string PositionsSource = """
        using System;
        using System.Runtime.CompilerServices;

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class Typed
        {
            public Typed(int literalLength, int formattedCount) { }

            public void AppendLiteral(string s) { }

            public void AppendFormatted<T>(T value) => Console.WriteLine(typeof(T).Name);

            public void AppendFormatted(Typed inner) { }
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class Tagged<T>
        {
            public Tagged(int literalLength, int formattedCount) { }

            public T? Last { get; private set; }

            public void AppendLiteral(string s) { }

            public void AppendFormatted(T value) => Last = value;
        }

        [Transpile]
        public sealed class Uses
        {
            private Typed _field = $"field{1}";

            public Uses(Typed t) { }

            public Typed Ret(int n) => $"r{n}";

            public Func<int, Typed> Lam() => n => $"l{n}";

            public Typed Cond(bool b, int n) => b ? (Typed)$"a{n}" : (Typed)$"b";

            public Uses Ctor(int n) => new Uses($"c{n}");

            public Tagged<int> Generic(int n) => $"g{n}";

            public void Nest(int n)
            {
                Typed nested = $"o{(Typed)$"i{n}"}";
                Typed inner = $"a{n}b{$"in{n}"}";
            }
        }
        """;

    private const string ArgumentsSource = """
        using System.Runtime.CompilerServices;

        [Transpile]
        public sealed class Logger
        {
            public void Log(int level, [InterpolatedStringHandlerArgument("", "level")] LogHandler h) { }
        }

        [Transpile]
        [InterpolatedStringHandler]
        public struct LogHandler
        {
            public LogHandler(int literalLength, int formattedCount, Logger logger, int level) { }

            public bool AppendLiteral(string s) => true;

            [Name("put")]
            public bool AppendFormatted(object? v, int alignment = 0, string? format = null) => true;
        }

        [Transpile]
        public sealed class Sink
        {
            private readonly Logger _logger = new();

            public void Write(int level, int n)
            {
                _logger.Log(level, $"a{n}b{n,3:X}");
                var logger = _logger;
                logger.Log(2, $"x{n}");
            }
        }
        """;

    private const string UnsupportedPrelude = """
        using System.Runtime.CompilerServices;

        [Transpile]
        public sealed class Lg
        {
            public void Log(int level, [InterpolatedStringHandlerArgument("", "level")] ArgH h) { }

            public void Log3(int level, int other, [InterpolatedStringHandlerArgument("level")] LevelH h) { }

            public void LogWide(int level, [InterpolatedStringHandlerArgument("level")] WideH h) { }

            public void LogOut(
                int level,
                out int z,
                [InterpolatedStringHandlerArgument("level")] LevelH h
            ) { z = level; }

            public static Lg Get() => new();
        }

        [Transpile]
        public sealed class Holder
        {
            public Lg B { get; } = new();
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class WideH
        {
            public WideH(int literalLength, int formattedCount, long level) { }

            public void AppendLiteral(string s) { }

            public void AppendFormatted<T>(T value) { }
        }

        [InterpolatedStringHandler]
        public sealed class RawH
        {
            public RawH(int literalLength, int formattedCount) { }

            public void AppendLiteral(string s) { }

            public void AppendFormatted<T>(T value) { }
        }

        [Transpile, PlainObject]
        [InterpolatedStringHandler]
        public sealed class ShapeH
        {
            public ShapeH(int literalLength, int formattedCount) { }

            public void AppendLiteral(string s) { }

            public void AppendFormatted<T>(T value) { }
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class ArgsH
        {
            [ObjectArgs]
            public ArgsH(int literalLength, int formattedCount) { }

            public void AppendLiteral(string s) { }

            public void AppendFormatted<T>(T value) { }
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class InlineH
        {
            public InlineH(int literalLength, int formattedCount) { }

            public void AppendLiteral(string s) { }

            [Inline]
            public void AppendFormatted(int value) { }
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class ArgH
        {
            public ArgH(int literalLength, int formattedCount, Lg lg, int level) { }

            public void AppendLiteral(string s) { }

            public void AppendFormatted<T>(T value) { }
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class LevelH
        {
            public LevelH(int literalLength, int formattedCount, int level) { }

            public void AppendLiteral(string s) { }

            public void AppendFormatted<T>(T value) { }
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class OkH
        {
            public OkH(int literalLength, int formattedCount, out bool ok) { ok = true; }

            public void AppendLiteral(string s) { }

            public void AppendFormatted<T>(T value) { }
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class PlainH
        {
            public PlainH(int literalLength, int formattedCount) { }

            public void AppendLiteral(string s) { }

            public void AppendFormatted<T>(T value) { }
        }

        [Transpile]
        [InterpolatedStringHandler]
        public sealed class EmitH
        {
            public EmitH(int literalLength, int formattedCount) { }

            public void AppendLiteral(string s) { }

            [Emit("$0.push($1)")]
            public void AppendFormatted(int value) { }
        }

        [Transpile]
        public sealed class Use
        {
            private static int Bump() => 1;

        """;

    [Test]
    public async Task TemplateCalls_BindTheExactOverloads()
    {
        var files = TranspileHelper.Transpile(TemplateSource);

        await TranspileHelper.AssertGolden("interpolated-string-handlers.ts", files["ui.ts"]);
    }

    [Test]
    public async Task Positions_HoistOrStayInPlace()
    {
        var files = TranspileHelper.Transpile(PositionsSource);

        await TranspileHelper.AssertGolden(
            "interpolated-string-handler-positions.ts",
            files["uses.ts"]
        );
    }

    [Test]
    public async Task HandlerArguments_PassTheReceiverAndArgumentAndChainBoolAppends()
    {
        var files = TranspileHelper.Transpile(ArgumentsSource);

        await TranspileHelper.AssertGolden(
            "interpolated-string-handler-arguments.ts",
            files["sink.ts"]
        );
    }

    [Test]
    [Arguments(TemplateSource)]
    [Arguments(PositionsSource)]
    [Arguments(ArgumentsSource)]
    public async Task LoweredShapes_ReportNothingAndTypeCheckStrictly(string source)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    /// <summary>
    /// Roslyn numbers a handler-argument placeholder by the written position of
    /// the argument it names, the order of the call's argument list, not by
    /// parameter order: named arguments out of parameter order still pass the
    /// named one.
    /// </summary>
    [Test]
    public async Task NamedArgumentsOutOfOrder_PassTheNamedArgument()
    {
        var files = TranspileHelper.Transpile(
            UnsupportedPrelude
                + """
                    public void Run(Lg lg, int n, int m) => lg.Log3(other: m, level: n, h: $"a{n}");
                }
                """
        );

        await Assert.That(files["use.ts"]).Contains("new LevelH(1, 1, n)");
    }

    /// <summary>
    /// A handler argument that is a repeatable read by the shared rule
    /// (<c>IrEvaluationTraits.IsRepeatableRead</c>) lowers, a plain
    /// auto-property included; an addition that lowers with overflow wrapping
    /// is not one.
    /// </summary>
    [Test]
    public async Task RepeatableHandlerArguments_AreReadAgainAtTheHandler()
    {
        var files = TranspileHelper.Transpile(
            UnsupportedPrelude
                + """
                    public int Level { get; set; }

                    public void Sum(Lg lg, int n) => lg.Log(n + 1, $"a{n}");

                    public void Auto(Lg lg) => lg.Log(Level, $"a{Level}");
                }
                """
        );

        await Assert.That(files["use.ts"]).Contains("new ArgH(1, 1, lg, this.level)");
        await Assert.That(files["use.ts"]).DoesNotContain("new ArgH(1, 1, lg, (n + 1)");
    }

    /// <summary>
    /// An <c>out</c> argument between a handler argument and the handler runs
    /// nothing before the handler, so it does not keep the argument from being
    /// read again.
    /// </summary>
    [Test]
    public async Task OutArgumentBetween_DoesNotBlockTheRead()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            UnsupportedPrelude
                + """
                    public int Run(Lg lg, int level)
                    {
                        lg.LogOut(level, out var z, $"a{level}");

                        return z;
                    }
                }
                """
        );

        await Assert
            .That(diagnostics.Where(d => d.Message.StartsWith("Interpolated string")))
            .IsEmpty();

        await Assert.That(files["use.ts"]).Contains("new LevelH(1, 1, level)");
    }

    /// <summary>
    /// A <c>base</c> receiver passes the instance: <c>super</c> is not a value.
    /// </summary>
    [Test]
    public async Task BaseReceiver_PassesThisAndTypeChecksStrictly()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Runtime.CompilerServices;

            [Transpile]
            public class Writer
            {
                public virtual void LogR([InterpolatedStringHandlerArgument("")] RecvH h) { }
            }

            [Transpile]
            public sealed class Loud : Writer
            {
                public override void LogR(RecvH h) { }

                public void Run(int level) => base.LogR($"b{level}");
            }

            [Transpile]
            [InterpolatedStringHandler]
            public sealed class RecvH
            {
                public RecvH(int literalLength, int formattedCount, Writer writer) { }

                public void AppendLiteral(string s) { }

                public void AppendFormatted(int value) { }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert.That(files["loud.ts"]).Contains("new RecvH(1, 1, this)");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    /// <summary>
    /// A C# 14 extension-block member whose handler names the extension
    /// receiver lowers with it. Naming it <c>""</c> does not compile (CS8944).
    /// </summary>
    [Test]
    public async Task ExtensionBlockReceiver_LowersWithTheReceiver()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Runtime.CompilerServices;

            [Transpile]
            public sealed class Pad { }

            [Transpile]
            public static class PadLogs
            {
                extension(Pad pad)
                {
                    public void LogX([InterpolatedStringHandlerArgument("pad")] PadH h) { }
                }
            }

            [Transpile]
            [InterpolatedStringHandler]
            public sealed class PadH
            {
                public PadH(int literalLength, int formattedCount, Pad pad) { }

                public void AppendLiteral(string s) { }

                public void AppendFormatted(int value) { }
            }

            [Transpile]
            public static class Use
            {
                public static void Run(Pad pad, int n) => pad.LogX($"x{n}");
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();

        await Assert
            .That(files["use.ts"])
            .Contains("logX(pad, (handler$temp$0 = new PadH(1, 1, pad), ");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task LibraryHandler_LowersInTheConsumerAndTypeChecksStrictly()
    {
        const string library = """
            using System.Runtime.CompilerServices;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("acme-lib")]

            namespace Acme
            {
                [InterpolatedStringHandler]
                public sealed class Line
                {
                    public Line(int literalLength, int formattedCount) { }

                    public void AppendLiteral(string s) { }

                    public void AppendFormatted(int value) { }

                    public void AppendFormatted(string value) { }
                }

                public static class Printer
                {
                    public static int Print(Line line) => 0;
                }
            }
            """;

        const string consumer = """
            [assembly: TranspileAssembly]

            namespace App
            {
                public static class Report
                {
                    public static int Run(int n, string s) => Acme.Printer.Print($"n={n} s={s}");
                }
            }
            """;

        var libraryFiles = TranspileHelper.Transpile(library);
        var consumerFiles = TranspileHelper.TranspileWithLibrary(library, consumer);

        await Assert
            .That(consumerFiles["app/report.ts"])
            .Contains("handler$temp$0.appendFormattedInt32(n)");

        await Assert
            .That(consumerFiles["app/report.ts"])
            .Contains("handler$temp$0.appendFormattedString(s)");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(libraryFiles, consumerFiles);

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task BclConversions_KeepTheTemplateLiteral()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;
            using System.Text;

            [Transpile]
            public static class Texts
            {
                public static string Plain(int n) => $"n={n}";

                public static string Formattable(int n)
                {
                    FormattableString f = $"f={n}";

                    return f.ToString();
                }

                public static string Built(int n) => new StringBuilder().Append($"b={n}").ToString();
            }
            """
        );

        var output = files["texts.ts"];

        await Assert.That(output).Contains("`n=${n}`");
        await Assert.That(output).Contains("`b=${n}`");
        await Assert.That(output).DoesNotContain("handler$temp");
    }

    [Test]
    [Arguments(
        "public void Run() { OkH h = $\"a{1}\"; }",
        "OkH",
        "its constructor takes the trailing 'out bool' parameter, which a constructor cannot lower"
    )]
    [Arguments(
        "public void Run(int level) { Lg.Get().Log(level, $\"a{level}\"); }",
        "ArgH",
        "the handler argument 'Lg.Get()' is not a repeatable read (a local, a parameter, 'this', "
            + "a constant, a field or plain auto-property of those, or an operator over them); "
            + "bind it to a local first"
    )]
    [Arguments(
        "public void Run(Lg lg) { lg.Log(Bump(), $\"a{1}\"); }",
        "ArgH",
        "the handler argument 'Bump()' is not a repeatable read (a local, a parameter, 'this', "
            + "a constant, a field or plain auto-property of those, or an operator over them); "
            + "bind it to a local first"
    )]
    [Arguments(
        "public void Run(Lg lg, int level) { lg.Log3(level, Bump(), $\"a{level}\"); }",
        "LevelH",
        "the argument 'Bump()' is evaluated between a handler argument and the handler and may "
            + "change it; bind it to a local first"
    )]
    [Arguments(
        "public void Run(Lg? maybe, int level) { maybe?.Log(level, $\"a{level}\"); }",
        "ArgH",
        "the receiver 'maybe' is null-conditional, so it cannot be read again at the handler; "
            + "write 'if (maybe != null) maybe.Log(...)' instead"
    )]
    [Arguments(
        "public void Run(Holder? a, int level) { a?.B.Log(level, $\"a{level}\"); }",
        "ArgH",
        "the receiver 'a?.B' is null-conditional, so it cannot be read again at the handler; "
            + "write 'if (a != null) a.B.Log(...)' instead"
    )]
    [Arguments(
        "public void Run(Lg lg, int level) { lg.LogWide(level, $\"a{level}\"); }",
        "WideH",
        "the handler argument 'level' is converted to the constructor's parameter type 'long'; "
            + "pass a value of that type"
    )]
    [Arguments(
        "public void Run(int n) { RawH h = $\"a{n}\"; }",
        "RawH",
        "the handler type is neither transpiled nor imported"
    )]
    [Arguments(
        "public void Run(int n) { ShapeH h = $\"a{n}\"; }",
        "ShapeH",
        "the handler type is a [PlainObject], whose object literal has no methods"
    )]
    [Arguments(
        "public void Run(int n) { ArgsH h = $\"a{n}\"; }",
        "ArgsH",
        "the handler constructor takes [ObjectArgs], whose factory takes no counts"
    )]
    [Arguments(
        "public void Run(int n) { InlineH h = $\"a{n}\"; }",
        "InlineH",
        "the part '{n}' calls an [Emit] or [Inline] member or binds dynamically"
    )]
    [Arguments(
        "public void Run(int n) { EmitH h = $\"a{n}\"; }",
        "EmitH",
        "the part '{n}' calls an [Emit] or [Inline] member or binds dynamically"
    )]
    [Arguments(
        "public void Run(dynamic d) { PlainH h = $\"a{d}\"; }",
        "PlainH",
        "the part '{d}' calls an [Emit] or [Inline] member or binds dynamically"
    )]
    public async Task UnsupportedShape_IsAnErrorAndCallsNoAppend(
        string member,
        string handler,
        string reason
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            UnsupportedPrelude + "    " + member + "\n}\n"
        );

        var error = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.UnsupportedFeature
            && d.Message.StartsWith("Interpolated string", StringComparison.Ordinal)
        );

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);

        await Assert
            .That(error.Message)
            .IsEqualTo(
                $"Interpolated string converted to the handler '{handler}' is not supported: "
                    + $"{reason} (FR-112)."
            );

        await Assert.That(files["use.ts"]).DoesNotContain("appendLiteral(");
    }

    [Test]
    public async Task Dart_ReportsEveryHandlerConversion()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            """
            using System.Runtime.CompilerServices;

            [Transpile]
            [InterpolatedStringHandler]
            public sealed class Line
            {
                public Line(int literalLength, int formattedCount) { }

                public void AppendLiteral(string s) { }

                public void AppendFormatted(int value) { }
            }

            [Transpile]
            public static class Use
            {
                public static Line Run(int n) => $"n={n}";
            }
            """
        );

        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);

        await Assert
            .That(error.Message)
            .IsEqualTo(
                "Interpolated string converted to the handler 'Line' is not supported: the Dart "
                    + "target does not lower handlers (FR-112)."
            );
    }
}
