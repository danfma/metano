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
/// <see cref="IrExtractionContext.FormatStrings"/> port decides which
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

    private const string FormattableInterfaceName = "System.IFormattable";

    /// <summary>The type names of the reports: minimally qualified, a nullable reference type without its <c>?</c>.</summary>
    private static readonly SymbolDisplayFormat TypeNameFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat.RemoveMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    /// <summary>Where an unsupported format sits, for the wording of its report.</summary>
    private enum FormattedConstruct
    {
        Hole,
        Call,
    }

    /// <summary>
    /// A <c>ToString(format)</c> call: the method it binds to, its format
    /// argument, its receiver (a member access's operand, or the bound
    /// receiver of a <c>?.</c> call), the
    /// receiver's static type and the number of arguments the call emits.
    /// </summary>
    private sealed record FormatCallSite(
        IMethodSymbol Method,
        ArgumentSyntax Format,
        ExpressionSyntax? Receiver,
        ITypeSymbol ReceiverType,
        int ArgumentCount
    );

    private IFormatStringCapability? FormatStrings => _context.FormatStrings;

    private string TargetName => _target?.ToString() ?? "this target";

    private IrInterpolationExpression ExtractInterpolationExpression(InterpolationSyntax hole)
    {
        var type = _semantic.GetTypeInfo(hole.Expression).Type;
        var kind = FormattedKindOf(type);
        var format = IgnoresFormat(type) ? null : hole.FormatClause?.FormatStringToken.ValueText;
        var alignment = InterpolationAlignment(hole);

        if (format is null && alignment is null)
            return new IrInterpolationExpression(ExtractInterpolationHole(hole), NumericKind: kind);

        if (FormatStrings is not { } formats)
            return UnsupportedHole(hole, UnsupportedClauseMessage());

        if (format is null || FormatsAsDotNet(kind, format, formats))
            return new IrInterpolationExpression(Extract(hole.Expression), format, alignment, kind);

        if (type is null || HasFormatter(kind, formats))
            return UnsupportedHole(hole, type, format);

        return ExtractUnformattedHole(hole, type, format, alignment, formats);
    }

    /// <summary>
    /// A hole whose format no runtime formatter takes, lowered as C# defines
    /// it, <c>value.ToString(format)</c>, in the order the call takes
    /// (<see cref="RewriteConstantFormatCall"/>): through the type's own
    /// <c>ToString(format)</c> when a declarative mapping lowers it, then the
    /// clause-free hole when the target prints that format as its default
    /// text (<see cref="IFormatStringCapability.PrintsAsDefaultText"/>), the
    /// reported placeholder otherwise. The alignment still pads the text. A
    /// value that may be <c>null</c> skips the mapping, which would receive
    /// the <c>null</c>; a call has no such receiver, so the two orders agree.
    /// </summary>
    private IrInterpolationExpression ExtractUnformattedHole(
        InterpolationSyntax hole,
        ITypeSymbol type,
        string format,
        int? alignment,
        IFormatStringCapability formats
    )
    {
        var valueType = type.UnwrapNullableValueType();
        var mayBeNull = type.IsNullableValueType() || type.IsReferenceType;

        if (!mayBeNull && MappedToStringWithFormat(valueType) is { } toString)
            return new IrInterpolationExpression(
                MappedFormatCall(Extract(hole.Expression), toString, format),
                Alignment: alignment
            );

        if (!formats.PrintsAsDefaultText(valueType, format))
            return UnsupportedHole(hole, type, format);

        return alignment is null
            ? new IrInterpolationExpression(ExtractInterpolationHole(hole))
            : new IrInterpolationExpression(Extract(hole.Expression), Alignment: alignment);
    }

    /// <summary>
    /// The last-but-one invocation rewriter
    /// (<see cref="Invocation.UnsupportedFormatCallRewriter"/>), for
    /// <c>ToString(format)</c> or <c>ToString(format, provider)</c> of a BCL
    /// type (<see cref="IsBclToStringWithFormat"/>): without a target
    /// capability it reports; otherwise a constant format follows
    /// <see cref="RewriteConstantFormatCall"/> and a format known only at run
    /// time <see cref="RewriteRunTimeFormatCall"/>. <c>null</c> for any other
    /// call.
    /// </summary>
    internal IrExpression? RewriteUnsupportedFormatCall(
        InvocationExpressionSyntax call,
        IMethodSymbol? symbol
    )
    {
        if (!IsBclToStringWithFormat(symbol) || FormatArgumentOf(call) is not { } argument)
            return null;

        if (FormatStrings is not { } formats)
            return ReportUnsupportedFormat(argument.GetLocation(), UnsupportedCallMessage());

        var receiver = (call.Expression as MemberAccessExpressionSyntax)?.Expression
            ?? ConditionalReceiverOf(call);

        var type = ReceiverTypeOf(receiver) ?? symbol.ContainingType;

        var format = _semantic.GetConstantValue(argument.Expression);

        if (!format.HasValue)
            return RewriteRunTimeFormatCall(argument, type, formats);

        return RewriteConstantFormatCall(
            new FormatCallSite(
                symbol,
                argument,
                receiver,
                type,
                EmittedArgumentCount(call, symbol)
            ),
            format.Value as string ?? "",
            formats
        );
    }

    /// <summary>The constant alignment of a hole (<c>{x,8}</c>), or <c>null</c> without one.</summary>
    private int? InterpolationAlignment(InterpolationSyntax hole) =>
        hole.AlignmentClause is { } clause
        && _semantic.GetConstantValue(clause.Value) is { HasValue: true, Value: int alignment }
            ? alignment
            : null;

    /// <summary>
    /// <c>ToString(format)</c> with a constant format, in four steps: the
    /// value's formatter (the call stays for the declarative mapping that
    /// calls it, or reports a format it rejects); the type's own mapping; the
    /// clause-free hole for a default-text format; the reported placeholder.
    /// </summary>
    private IrExpression? RewriteConstantFormatCall(
        FormatCallSite site,
        string format,
        IFormatStringCapability formats
    )
    {
        var kind = FormattedKindOf(site.ReceiverType);

        if (HasFormatter(kind, formats))
            return FormatsAsDotNet(kind, format, formats) ? null : UnsupportedCall(site, format);

        if (LowersByDeclarativeMapping(site.Method, site.ArgumentCount))
            return null;

        if (site.Receiver is { } receiver && formats.PrintsAsDefaultText(site.ReceiverType, format))
            return new IrStringInterpolation([new IrInterpolationExpression(Extract(receiver))]);

        return UnsupportedCall(site, format);
    }

    /// <summary>
    /// <c>ToString(format)</c> whose format is known only at run time:
    /// <c>null</c> where the target's formatter decides it (it throws
    /// <c>FormatException</c> or <c>NotSupportedException</c> for a format it
    /// does not implement), the reported placeholder otherwise.
    /// </summary>
    private IrUnsupportedExpression? RewriteRunTimeFormatCall(
        ArgumentSyntax argument,
        ITypeSymbol type,
        IFormatStringCapability formats
    )
    {
        if (HasFormatter(FormattedKindOf(type), formats))
            return null;

        return ReportUnsupportedFormat(
            argument.GetLocation(),
            $"Format strings are not supported for '{DisplayNameOf(type)}' in {TargetName}; "
                + "the call throws at run time."
        );
    }

    private IrUnsupportedExpression UnsupportedCall(FormatCallSite site, string format) =>
        ReportUnsupportedFormat(
            site.Format.GetLocation(),
            UnsupportedFormatMessage(site.ReceiverType, format, FormattedConstruct.Call)
        );

    private IrInterpolationExpression UnsupportedHole(
        InterpolationSyntax hole,
        ITypeSymbol? type,
        string format
    ) => UnsupportedHole(hole, UnsupportedFormatMessage(type, format, FormattedConstruct.Hole));

    private static IrInterpolationExpression UnsupportedHole(
        InterpolationSyntax hole,
        string message
    ) => new(ReportUnsupportedFormat(hole.GetLocation(), message));

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
    private static bool HasFormatter(IrNumericKind? kind, IFormatStringCapability formats) =>
        kind is { } numericKind && formats.HasFormatter(numericKind);

    /// <summary>The static type of a call's receiver, looking through <c>Nullable&lt;T&gt;</c>.</summary>
    private ITypeSymbol? ReceiverTypeOf(ExpressionSyntax? receiver) =>
        TypeOf(receiver)?.UnwrapNullableValueType();

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
    private static bool FormatsAsDotNet(
        IrNumericKind? kind,
        string format,
        IFormatStringCapability formats
    ) =>
        kind is { } numericKind
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
    /// Whether <paramref name="symbol"/> is an instance <c>ToString</c>
    /// overload whose first parameter is the <c>format</c>, declared by a BCL
    /// type (a static <c>ToString</c>, such as <c>Convert.ToString(string)</c>,
    /// is not a format call) (<see cref="RoslynTypeQueries.IsBclAssembly"/>):
    /// a number's, an enum's (<c>Enum.ToString(string)</c>), a date or time's,
    /// <c>Guid</c>'s, or <c>IFormattable.ToString</c> on a type parameter. A
    /// user type's own <c>ToString(string)</c>, in this compilation or in any
    /// referenced library or package, is its own method and is left alone.
    /// </summary>
    private static bool IsBclToStringWithFormat([NotNullWhen(true)] IMethodSymbol? symbol) =>
        symbol
            is {
                Name: nameof(ToString),
                IsStatic: false,
                Parameters: [
                    { Name: FormatParameterName, Type.SpecialType: SpecialType.System_String },
                    ..,
                ]
            }
        && symbol.ContainingAssembly.IsBclAssembly();

    /// <summary>
    /// The receiver of <c>value?.ToString(format)</c>, however the call sits in
    /// the conditional chain (<c>m?.ToString("G").Length</c>,
    /// <c>m?.ToString("G")?.Length</c>): the operand of the conditional access
    /// whose binding the call is, which the continuation extracts as the bound,
    /// non-null receiver; <c>null</c> for any other call.
    /// </summary>
    private static ExpressionSyntax? ConditionalReceiverOf(InvocationExpressionSyntax call) =>
        call.Expression is MemberBindingExpressionSyntax binding
            ? QualifiedMemberSyntax.ConditionalReceiverOf(binding)
            : null;

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
        type.AllInterfaces.Any(contract => contract.ToDisplayString() == FormattableInterfaceName);

    private static string NameOf(FormattedConstruct construct) =>
        construct switch
        {
            FormattedConstruct.Hole => "hole",
            FormattedConstruct.Call => "call",
            _ => throw new ArgumentOutOfRangeException(nameof(construct)),
        };

    private static string DisplayNameOf(ITypeSymbol? type) =>
        type?.ToDisplayString(TypeNameFormat) ?? "object";
}
