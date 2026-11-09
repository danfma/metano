using System.Globalization;
using System.Text.RegularExpressions;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The formatting and parsing side of spec 012's 64-bit BCL mappings
/// (FR-037, FR-038): an interpolation hole over a <c>long</c> or
/// <c>ulong</c> with a format or an alignment calls <c>formatInt64</c>, and
/// <c>MS0066</c> warns about a constant format or <c>NumberStyles</c> the
/// runtime does not implement. The supported sets mirror
/// <c>metano-runtime</c>'s <c>int64-format.ts</c> and
/// <c>int64-parsing.ts</c>.
/// </summary>
internal static partial class Int64Formatting
{
    private const string Int64TypeName = "long";

    private const string UInt64TypeName = "ulong";

    /// <summary>The standard formats .NET defines for integers that <c>formatInt64</c> does not implement.</summary>
    private static readonly HashSet<char> UnsupportedStandardFormats = ['B', 'C', 'E', 'P'];

    /// <summary>The styles <c>int64-parsing.ts</c> implements.</summary>
    private static readonly HashSet<int> SupportedNumberStyles =
    [
        (int)NumberStyles.Integer,
        (int)NumberStyles.HexNumber,
        (int)(NumberStyles.Integer | NumberStyles.AllowThousands),
        (int)NumberStyles.Number,
    ];

    /// <summary>
    /// <c>formatInt64(value, format, alignment)</c> for a 64-bit hole with a
    /// format or an alignment, reporting <c>MS0066</c> for an unsupported
    /// constant format; <c>null</c> for any other hole, which keeps the plain
    /// template-literal hole.
    /// </summary>
    public static TsExpression? TryFormatHole(IrInterpolationExpression hole, TsExpression value)
    {
        var formatsItself =
            hole.SixtyFourBitKind is not null
            && (hole.FormatSpecifier is not null || hole.Alignment is not null);

        if (!formatsItself)
            return null;

        ReportIfUnsupportedFormat(hole.FormatSpecifier);

        TsExpression format = hole.FormatSpecifier is { } specifier
            ? new TsStringLiteral(specifier)
            : new TsLiteral("null");

        TsExpression[] arguments = hole.Alignment is { } alignment
            ? [value, format, new TsLiteral(alignment.ToString(CultureInfo.InvariantCulture))]
            : [value, format];

        return Int64TsExpressions.RuntimeCall(Int64RuntimeHelpers.FormatInt64, arguments);
    }

    /// <summary>
    /// Reports <c>MS0066</c> for a call of <c>ToString(format)</c>,
    /// <c>Parse</c> or <c>TryParse</c> on <c>long</c> / <c>ulong</c> whose
    /// constant format or <c>NumberStyles</c> the runtime does not implement.
    /// </summary>
    public static void ReportUnsupportedFormatOrStyle(IrCallExpression call)
    {
        if (!IsSixtyFourBitMember(call.Origin))
            return;

        switch (call.Origin!.MemberName)
        {
            case "ToString"
                when call.Arguments is [{ Value: IrLiteral { Value: string format } }, ..]:
                ReportIfUnsupportedFormat(format);
                break;
            case "Parse" or "TryParse":
                ReportIfUnsupportedStyle(call.Arguments);
                break;
        }
    }

    private static bool IsSixtyFourBitMember(IrMemberOrigin? origin) =>
        origin?.DeclaringTypeFullName is Int64TypeName or UInt64TypeName;

    private static void ReportIfUnsupportedFormat(string? format)
    {
        if (format is null || !IsUnsupportedFormat(format))
            return;

        Report(
            $"Format '{format}' is not supported for 64-bit integers in TypeScript; the call "
                + "throws NotSupportedException at run time (spec 012 FR-037)."
        );
    }

    /// <summary>
    /// A valid .NET format that <c>formatInt64</c> rejects: a custom format
    /// (anything but a letter and an optional precision) or one of the
    /// unimplemented standard letters. An invalid standard letter (<c>Q</c>)
    /// is not reported: .NET throws <c>FormatException</c> for it too.
    /// </summary>
    private static bool IsUnsupportedFormat(string format)
    {
        if (format.Length == 0)
            return false;

        return !StandardFormat().IsMatch(format)
            || UnsupportedStandardFormats.Contains(char.ToUpperInvariant(format[0]));
    }

    /// <summary>
    /// Reports <c>MS0066</c> for a constant <c>NumberStyles</c> (the second
    /// argument, folded to its flags value by the core) the runtime does not
    /// implement.
    /// </summary>
    private static void ReportIfUnsupportedStyle(IReadOnlyList<IrArgument> arguments)
    {
        if (arguments is not [_, { Value: IrLiteral { Value: int style } }, ..])
            return;

        if (SupportedNumberStyles.Contains(style))
            return;

        Report(
            $"NumberStyles value {style} is not supported for 64-bit integers in TypeScript; "
                + "the call throws NotSupportedException at run time (spec 012 FR-038)."
        );
    }

    private static void Report(string message) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedInt64Format,
                message
            )
        );

    [GeneratedRegex("^[A-Za-z][0-9]*$")]
    private static partial Regex StandardFormat();
}
