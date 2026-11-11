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
/// A format on a value C# does not pass it to (one that is not
/// <c>IFormattable</c>, and <c>char</c>) is dropped, because C# ignores it
/// too.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string UnsupportedFormatKind = "UnsupportedFormat";

    private const string FormatParameterName = "format";

    /// <summary>The arguments of the <c>value.ToString("format")</c> a hole lowers through: the format.</summary>
    private const int MappedFormatArgumentCount = 1;

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

        if (NumericFormats is not { } formats)
            return new IrInterpolationExpression(
                ReportUnsupportedFormat(hole.GetLocation(), UnsupportedClauseMessage())
            );

        if (format is null || FormatsAsDotNet(kind, format))
            return new IrInterpolationExpression(Extract(hole.Expression), format, alignment, kind);

        return type is null
            ? new IrInterpolationExpression(
                ReportUnsupportedFormat(
                    hole.GetLocation(),
                    UnsupportedFormatMessage(type, format, FormattedConstruct.Hole)
                )
            )
            : ExtractUnformattedHole(hole, type, format, alignment, formats);
    }

    /// <summary>
    /// A hole whose format no runtime formatter takes, lowered as C# defines
    /// it, <c>value.ToString(format)</c>: the clause-free hole when the
    /// target already prints that format as its default text
    /// (<see cref="INumericFormatCapability.PrintsAsDefaultText"/>), the
    /// type's own <c>ToString(format)</c> mapping when a declarative mapping
    /// lowers it (so a hole and the call cannot disagree), the reported
    /// placeholder otherwise. The alignment still pads the text.
    /// </summary>
    private IrInterpolationExpression ExtractUnformattedHole(
        InterpolationSyntax hole,
        ITypeSymbol type,
        string format,
        int? alignment,
        INumericFormatCapability formats
    )
    {
        var value = type.UnwrapNullableValueType();

        if (HasFormatter(FormattedKindOf(value), formats))
            return new IrInterpolationExpression(
                ReportUnsupportedFormat(
                    hole.GetLocation(),
                    UnsupportedFormatMessage(type, format, FormattedConstruct.Hole)
                )
            );

        if (formats.PrintsAsDefaultText(value, format))
            return alignment is null
                ? new IrInterpolationExpression(ExtractInterpolationHole(hole))
                : new IrInterpolationExpression(Extract(hole.Expression), Alignment: alignment);

        var isNullable = type.IsNullableValueType() || type.IsReferenceType;

        if (!isNullable && MappedToStringWithFormat(value) is { } toString)
            return new IrInterpolationExpression(
                MappedFormatCall(Extract(hole.Expression), toString, format),
                Alignment: alignment
            );

        return new IrInterpolationExpression(
            ReportUnsupportedFormat(
                hole.GetLocation(),
                UnsupportedFormatMessage(type, format, FormattedConstruct.Hole)
            )
        );
    }

    /// <summary>
    /// The last-but-one invocation rewriter
    /// (<see cref="Invocation.UnsupportedFormatCallRewriter"/>):
    /// <c>ToString(format)</c> or <c>ToString(format, provider)</c> of a BCL
    /// type (<see cref="IsBclToStringWithFormat"/>) on a value whose format
    /// the target does not format as .NET does reports and becomes the
    /// placeholder; a format known only at run time is left to the target's
    /// formatter; a format the target prints as its default text becomes the
    /// clause-free hole; <c>null</c> for any other call, which an existing
    /// declarative mapping, or the 64-bit integers' own check (MS0066),
    /// lowers.
    /// </summary>
    internal IrExpression? RewriteUnsupportedFormatCall(
        InvocationExpressionSyntax call,
        IMethodSymbol? symbol
    )
    {
        if (!IsBclToStringWithFormat(symbol))
            return null;

        if (FormatArgumentOf(call) is not { } argument)
            return null;

        if (NumericFormats is not { } formats)
            return ReportUnsupportedFormat(argument.GetLocation(), UnsupportedCallMessage());

        var receiver = (call.Expression as MemberAccessExpressionSyntax)?.Expression;
        var type = TypeOf(receiver) ?? symbol.ContainingType;
        var kind = FormattedKindOf(type);

        if (kind is IrNumericKind.Int64 or IrNumericKind.UInt64)
            return null;

        var format = _semantic.GetConstantValue(argument.Expression);

        if (!format.HasValue)
            return RewriteRunTimeFormatCall(argument, type, kind, formats);

        var constantFormat = format.Value as string ?? "";

        if (FormatsAsDotNet(kind, constantFormat))
            return null;

        if (
            !HasFormatter(kind, formats)
            && LowersByDeclarativeMapping(symbol, EmittedArgumentCount(call, symbol))
        )
            return null;

        if (receiver is not null && formats.PrintsAsDefaultText(type, constantFormat))
            return new IrStringInterpolation([
                new IrInterpolationExpression(ConcatenationOperand(receiver)),
            ]);

        return ReportUnsupportedFormat(
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
    /// does not implement) or a declarative mapping lowers the call, the
    /// reported placeholder otherwise.
    /// </summary>
    private IrUnsupportedExpression? RewriteRunTimeFormatCall(
        ArgumentSyntax argument,
        ITypeSymbol type,
        IrNumericKind? kind,
        INumericFormatCapability formats
    )
    {
        if (HasFormatter(kind, formats))
            return null;

        return ReportUnsupportedFormat(
            argument.GetLocation(),
            $"Format strings are not supported for '{DisplayNameOf(type)}' in {TargetName}; "
                + "the call throws at run time."
        );
    }

    /// <summary>
    /// The <c>ToString(string)</c> of <paramref name="type"/> (or of a base
    /// type, such as <c>Enum.ToString(string)</c>) when a declarative mapping
    /// of the target lowers it; <c>null</c> otherwise.
    /// </summary>
    private IMethodSymbol? MappedToStringWithFormat(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var toString = current
                .GetMembers(nameof(ToString))
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method =>
                    method.Parameters is [{ Type.SpecialType: SpecialType.System_String }]
                );

            if (toString is not null)
                return LowersByDeclarativeMapping(toString, MappedFormatArgumentCount)
                    ? toString
                    : null;
        }

        return null;
    }

    /// <summary>
    /// <c>value.ToString("format")</c> as the call extraction builds it, so
    /// the target's mapping of the call lowers it.
    /// </summary>
    private IrCallExpression MappedFormatCall(
        IrExpression value,
        IMethodSymbol toString,
        string format
    )
    {
        var origin = BuildOrigin(toString);

        return new IrCallExpression(
            new IrMemberAccess(value, toString.Name, origin),
            [new IrArgument(new IrLiteral(format, IrLiteralKind.String))],
            Origin: origin
        );
    }

    /// <summary>
    /// Whether a runtime formatter decides the formats of <paramref name="kind"/>:
    /// then it alone does, and a format it does not take reports rather than
    /// reaching a mapping of the same formatter.
    /// </summary>
    private static bool HasFormatter(IrNumericKind? kind, INumericFormatCapability formats) =>
        kind is { } numericKind && formats.HasFormatter(numericKind);

    private ITypeSymbol? TypeOf(ExpressionSyntax? expression) =>
        expression is null ? null : _semantic.GetTypeInfo(expression).Type;

    /// <summary>
    /// Whether a declarative mapping of the target lowers
    /// <paramref name="method"/> called with <paramref name="argumentCount"/>
    /// arguments, the count the call emits (<see cref="EmittedArgumentCount"/>);
    /// <c>false</c> without the port, so the call reports.
    /// </summary>
    private bool LowersByDeclarativeMapping(IMethodSymbol method, int argumentCount) =>
        _context.LowersByDeclarativeMapping?.Invoke(method, argumentCount) ?? false;

    /// <summary>Why the target cannot honor a hole's clauses: it declares no format capability.</summary>
    private string UnsupportedClauseMessage() =>
        "Format and alignment clauses of an interpolation hole are not supported in "
        + $"{TargetName}; the hole throws at run time.";

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
        $"Format strings are not supported in {TargetName}; the call throws at run time.";

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
    /// a format declared by a BCL type (in metadata): a number's, an enum's
    /// (<c>Enum.ToString(string)</c>), a date or time's, <c>Guid</c>'s, or
    /// <c>IFormattable.ToString</c> on a type parameter. A user type's own
    /// <c>ToString(string)</c> is its own method and is left alone.
    /// </summary>
    private static bool IsBclToStringWithFormat([NotNullWhen(true)] IMethodSymbol? symbol) =>
        symbol
            is {
                Name: nameof(ToString),
                Parameters: [{ Type.SpecialType: SpecialType.System_String }, ..]
            }
        && symbol.Locations.All(location => location.IsInMetadata);

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

    /// <summary>
    /// Whether C# ignores a format for a value of <paramref name="type"/>:
    /// composite formatting passes the format only to an
    /// <c>IFormattable</c>, so a static type that does not implement it (a
    /// <c>string</c>, a <c>bool</c>, a user class, record or struct) prints
    /// its <c>ToString()</c>; <c>char</c> implements it and ignores the
    /// format. <c>object</c>, an interface and a type parameter keep it: the
    /// run-time type decides.
    /// </summary>
    private static bool IgnoresFormat(ITypeSymbol? type)
    {
        if (type?.UnwrapNullableValueType() is not { } value)
            return false;

        if (value.SpecialType is SpecialType.System_Char)
            return true;

        var runTimeTypeDecides =
            value is ITypeParameterSymbol or IDynamicTypeSymbol or IErrorTypeSymbol
            || value.TypeKind is TypeKind.Interface
            || value.SpecialType is SpecialType.System_Object;

        return !runTimeTypeDecides && !ImplementsIFormattable(value);
    }

    private static bool ImplementsIFormattable(ITypeSymbol type) =>
        type.AllInterfaces.Any(contract => contract.ToDisplayString() == "System.IFormattable");

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
