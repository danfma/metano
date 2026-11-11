using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The format and alignment clauses of interpolation holes
/// (<c>{n,8:D2}</c>) and the format of <c>ToString(format)</c> on the
/// numbers (baseline FR-051, #323). The target's
/// <see cref="IrExtractionContext.NumericFormats"/> port decides which
/// formats it honors; a clause it cannot honor reports MS0001 at the hole or
/// the format argument and lowers to the throwing placeholder, so no clause
/// is dropped silently. Without the port every clause reports (fail closed).
/// The format of a <c>string</c>, <c>bool</c> or <c>char</c> is dropped,
/// because C# ignores it too.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string UnsupportedFormatKind = "UnsupportedFormat";

    private const string FormatParameterName = "format";

    /// <summary>Where an unsupported format sits, for the wording of its report.</summary>
    private enum FormattedConstruct
    {
        Hole,
        Call,
    }

    private INumericFormatCapability? NumericFormats => _context.NumericFormats;

    private string TargetName => _target?.ToString() ?? "this target";

    private IrInterpolationExpression ExtractInterpolationExpression(InterpolationSyntax hole)
    {
        var type = _semantic.GetTypeInfo(hole.Expression).Type;
        var kind = FormattedKindOf(type);
        var format = IgnoresFormat(type) ? null : hole.FormatClause?.FormatStringToken.ValueText;
        var alignment = InterpolationAlignment(hole);

        if (format is null && alignment is null)
            return new IrInterpolationExpression(ExtractInterpolationHole(hole), NumericKind: kind);

        if (UnsupportedClauseMessage(type, kind, format) is { } message)
            return new IrInterpolationExpression(
                ReportUnsupportedFormat(hole.GetLocation(), message)
            );

        return new IrInterpolationExpression(Extract(hole.Expression), format, alignment, kind);
    }

    /// <summary>
    /// The last-but-one invocation rewriter
    /// (<see cref="Invocation.UnsupportedFormatCallRewriter"/>):
    /// <c>ToString(format)</c> or <c>ToString(format, provider)</c> on a
    /// number it governs (<see cref="IsNumericToStringWithFormat"/>) whose
    /// constant format the target does not format as .NET does, or whose
    /// value has no formatter, reports and becomes the placeholder; a format
    /// known only at run time is left to the target's formatter; <c>null</c>
    /// for any other call, which the declarative mapping lowers.
    /// </summary>
    internal IrExpression? RewriteUnsupportedFormatCall(
        InvocationExpressionSyntax call,
        IMethodSymbol? symbol
    )
    {
        if (!IsNumericToStringWithFormat(symbol))
            return null;

        if (FormatArgumentOf(call) is not { } argument)
            return null;

        if (NumericFormats is not { } formats)
            return ReportUnsupportedFormat(argument.GetLocation(), UnsupportedCallMessage());

        var type = symbol.ContainingType;
        var format = _semantic.GetConstantValue(argument.Expression);

        if (!format.HasValue)
            return RewriteRunTimeFormatCall(argument, type, formats);

        var constantFormat = format.Value as string ?? "";

        return FormatsAsDotNet(FormattedKindOf(type), constantFormat)
            ? null
            : ReportUnsupportedFormat(
                argument.GetLocation(),
                UnsupportedFormatMessage(type, constantFormat, FormattedConstruct.Call)
            );
    }

    /// <summary>The constant alignment of a hole (<c>{x,8}</c>), or <c>null</c> without one.</summary>
    private int? InterpolationAlignment(InterpolationSyntax hole) =>
        hole.AlignmentClause is { } clause
        && _semantic.GetConstantValue(clause.Value) is { HasValue: true, Value: int alignment }
            ? alignment
            : null;

    /// <summary>
    /// <c>ToString(format)</c> whose format is known only at run time:
    /// <c>null</c> where the target's formatter decides it (it throws
    /// <c>FormatException</c> or <c>NotSupportedException</c> for a format it
    /// does not implement), the reported placeholder otherwise.
    /// </summary>
    private IrUnsupportedExpression? RewriteRunTimeFormatCall(
        ArgumentSyntax argument,
        ITypeSymbol type,
        INumericFormatCapability formats
    )
    {
        if (FormattedKindOf(type) is { } kind && formats.HasFormatter(kind))
            return null;

        return ReportUnsupportedFormat(
            argument.GetLocation(),
            $"Format strings are not supported for '{DisplayNameOf(type)}' in {TargetName}; "
                + "the call throws at run time."
        );
    }

    /// <summary>
    /// Why the target cannot honor a hole's clauses, or <c>null</c> when it
    /// can: an alignment pads any value, a format needs a formatter for the
    /// value's numeric kind that implements it.
    /// </summary>
    private string? UnsupportedClauseMessage(ITypeSymbol? type, IrNumericKind? kind, string? format)
    {
        if (NumericFormats is null)
            return "Format and alignment clauses of an interpolation hole are not supported in "
                + $"{TargetName}; the hole throws at run time.";

        if (format is null || FormatsAsDotNet(kind, format))
            return null;

        return UnsupportedFormatMessage(type, format, FormattedConstruct.Hole);
    }

    /// <summary>
    /// Whether the target formats a value of <paramref name="kind"/> with the
    /// constant <paramref name="format"/> as .NET does.
    /// </summary>
    private bool FormatsAsDotNet(IrNumericKind? kind, string format) =>
        NumericFormats is { } formats
        && kind is { } numericKind
        && formats.HasFormatter(numericKind)
        && formats.Formats(numericKind, format);

    private string UnsupportedFormatMessage(
        ITypeSymbol? type,
        string format,
        FormattedConstruct construct
    ) =>
        $"The format '{format}' is not supported for '{DisplayNameOf(type)}' in {TargetName}; "
        + $"the {NameOf(construct)} throws at run time.";

    private string UnsupportedCallMessage() =>
        $"Numeric format strings are not supported in {TargetName}; the call throws at run time.";

    private static IrUnsupportedExpression ReportUnsupportedFormat(
        Location location,
        string message
    )
    {
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                message,
                location
            )
        );

        return new IrUnsupportedExpression(UnsupportedFormatKind);
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> is a <c>ToString</c> overload taking
    /// a format on a number whose formats this policy governs: the integers
    /// of 32 bits or fewer, <c>double</c> and <c>float</c>.
    /// <para>
    /// TODO(#662): <c>long</c> / <c>ulong</c> are left out because the
    /// TypeScript bridge reports their formats as MS0066, without a location;
    /// unifying the two diagnostics brings them under this port.
    /// <c>decimal</c> and <c>BigInteger</c> are not governed yet: their
    /// <c>ToString(format)</c> keeps its source form on both targets.
    /// </para>
    /// </summary>
    private static bool IsNumericToStringWithFormat([NotNullWhen(true)] IMethodSymbol? symbol) =>
        symbol
            is {
                Name: nameof(ToString),
                Parameters: [{ Type.SpecialType: SpecialType.System_String }, ..]
            }
        && symbol.ContainingType.SpecialType
            is SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Double
                or SpecialType.System_Single;

    /// <summary>The argument passed as <c>ToString</c>'s <c>format</c>, by name or first by position.</summary>
    private static ArgumentSyntax? FormatArgumentOf(InvocationExpressionSyntax call)
    {
        var arguments = call.ArgumentList.Arguments;

        return arguments.FirstOrDefault(argument =>
                argument.NameColon?.Name.Identifier.ValueText == FormatParameterName
            ) ?? arguments.FirstOrDefault(argument => argument.NameColon is null);
    }

    /// <summary>The numeric kind of a value, looking through <c>Nullable&lt;T&gt;</c>; <c>null</c> for an enum or a non-numeric type.</summary>
    private static IrNumericKind? FormattedKindOf(ITypeSymbol? type) =>
        NumericEndpoints.Of(type) is { IsEnum: false } endpoint ? endpoint.Kind : null;

    /// <summary>Whether C# ignores a format for a value of <paramref name="type"/>: a <c>string</c>, <c>bool</c> or <c>char</c>.</summary>
    private static bool IgnoresFormat(ITypeSymbol? type) =>
        type?.UnwrapNullableValueType().SpecialType
            is SpecialType.System_String
                or SpecialType.System_Boolean
                or SpecialType.System_Char;

    private static string NameOf(FormattedConstruct construct) =>
        construct switch
        {
            FormattedConstruct.Hole => "hole",
            FormattedConstruct.Call => "call",
            _ => throw new ArgumentOutOfRangeException(nameof(construct)),
        };

    private static string DisplayNameOf(ITypeSymbol? type) =>
        type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "object";
}
