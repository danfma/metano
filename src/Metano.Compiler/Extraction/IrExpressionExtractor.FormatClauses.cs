using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The format and alignment clauses of interpolation holes
/// (<c>{n,8:D2}</c>) and the format of <c>ToString(format)</c> on the
/// numbers (baseline FR-051, #323). A clause the target honors stays on the
/// <see cref="IrInterpolationExpression"/>; one it cannot honor reports
/// MS0001 at the hole or the format argument and lowers to the throwing
/// placeholder, so no clause is dropped silently. The format of a
/// <c>string</c>, <c>bool</c> or <c>char</c> is dropped, because C# ignores
/// it too.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string UnsupportedFormatKind = "UnsupportedFormat";

    private const string FormatParameterName = "format";

    /// <summary>
    /// Whether the target formats numbers with .NET's standard format strings:
    /// TypeScript, through <c>metano-runtime</c>. Dart honors no clause yet.
    /// </summary>
    private bool HonorsFormatClauses => _target is not Metano.Annotations.TargetLanguage.Dart;

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
    /// 32-bit or narrower integer, a <c>double</c> or a <c>float</c> whose
    /// format the target does not format as .NET does — a format that is not
    /// a constant included — reports and becomes the placeholder; <c>null</c>
    /// for any other call, which the declarative mapping lowers. The 64-bit
    /// integers keep their own check (MS0066, spec 012 FR-037).
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

        var type = symbol.ContainingType;
        var format = _semantic.GetConstantValue(argument.Expression);

        if (HonorsFormatClauses && !format.HasValue)
            return ReportUnsupportedFormat(
                argument.GetLocation(),
                $"A format that is not a constant is not supported for '{DisplayNameOf(type)}' in "
                    + "TypeScript; the call throws at run time."
            );

        var constantFormat = format.Value as string ?? "";

        return FormatsAsDotNet(FormattedKindOf(type), constantFormat)
            ? null
            : ReportUnsupportedFormat(
                argument.GetLocation(),
                UnsupportedFormatMessage(type, constantFormat, "call")
            );
    }

    /// <summary>The constant alignment of a hole (<c>{x,8}</c>), or <c>null</c> without one.</summary>
    private int? InterpolationAlignment(InterpolationSyntax hole) =>
        hole.AlignmentClause is { } clause
        && _semantic.GetConstantValue(clause.Value) is { HasValue: true, Value: int alignment }
            ? alignment
            : null;

    /// <summary>
    /// Why the target cannot honor a hole's clauses, or <c>null</c> when it
    /// can: an alignment pads any value, a format needs a runtime formatter
    /// for the value's numeric kind that implements it.
    /// </summary>
    private string? UnsupportedClauseMessage(ITypeSymbol? type, IrNumericKind? kind, string? format)
    {
        if (!HonorsFormatClauses)
            return "Format and alignment clauses of an interpolation hole are not supported in "
                + "Dart; the hole throws at run time.";

        if (format is null || FormatsAsDotNet(kind, format))
            return null;

        return UnsupportedFormatMessage(type, format, "hole");
    }

    /// <summary>
    /// Whether the target's runtime formats a value of <paramref name="kind"/>
    /// with the constant <paramref name="format"/> as .NET does, an invalid
    /// format included, which both reject with the same <c>FormatException</c>.
    /// </summary>
    private bool FormatsAsDotNet(IrNumericKind? kind, string format) =>
        HonorsFormatClauses
        && kind is { } numericKind
        && NumericFormatSupport.HasFormatter(numericKind)
        && NumericFormatSupport.Supports(numericKind, format);

    private string UnsupportedFormatMessage(ITypeSymbol? type, string format, string construct) =>
        HonorsFormatClauses
            ? $"The format '{format}' is not supported for '{DisplayNameOf(type)}' in TypeScript; "
                + $"the {construct} throws at run time."
            : $"Numeric format strings are not supported in Dart; the {construct} throws at run time.";

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

    private static string DisplayNameOf(ITypeSymbol? type) =>
        type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "object";
}
