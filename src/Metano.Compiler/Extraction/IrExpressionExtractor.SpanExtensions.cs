using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The <c>System.MemoryExtensions</c> calls (#657). C# 14 converts an array
/// (and a string) to a span implicitly, so <c>array.SequenceEqual(other)</c>,
/// <c>array.Sort()</c> or <c>array.Contains(x, comparer)</c> bind to the span
/// extensions. Left to the plain call lowering, each would become a member
/// call on the receiver (<c>array.sequenceEqual(…)</c>) that a JavaScript
/// array does not have. A call no declarative mapping of the target lowers
/// reports MS0001 and lowers to the throwing placeholder, as an unlowered
/// LINQ call does. Two rules sit beside the mappings:
/// <list type="bullet">
///   <item>
///     A string search (<c>StartsWith</c>, <c>EndsWith</c>, the <c>Trim</c>
///     family, the subsequence <c>IndexOf</c> / <c>LastIndexOf</c>) lowers to
///     the JavaScript string method of the same name, so it lowers only on a
///     receiver that is a string: a <c>string</c> or a
///     <c>ReadOnlySpan&lt;char&gt;</c>. On an array (<c>char[]</c> included)
///     or a <c>Span&lt;char&gt;</c>, both JavaScript arrays, it reports.
///   </item>
///   <item>
///     The comparer of <c>Contains</c> / <c>IndexOf</c> / <c>LastIndexOf</c>,
///     when it is omitted or the <c>null</c> constant, asks for the default
///     comparison: the call lowers as the comparer-less overload, without the
///     comparer argument (as a LINQ stage does, #648).
///   </item>
/// </list>
/// A target without a mappings port (Dart) keeps every call as it is.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string MemoryExtensionsTypeName = "System.MemoryExtensions";

    private const string UnloweredSpanCallKind = "UnloweredSpanCall";

    private const string ReadOnlySpanTypeName = "System.ReadOnlySpan<T>";

    private static readonly HashSet<string> StringSearchMethods =
    [
        "StartsWith",
        "EndsWith",
        "Trim",
        "TrimStart",
        "TrimEnd",
    ];

    private static readonly HashSet<string> ValueSearchMethods =
    [
        "Contains",
        "IndexOf",
        "LastIndexOf",
    ];

    /// <summary>
    /// An invocation rewriter after every lowering one
    /// (<see cref="Invocation.UnsupportedSpanCallRewriter"/>): a
    /// <c>MemoryExtensions</c> call no declarative mapping of the target
    /// lowers reports and becomes the placeholder, and a value search with a
    /// <c>null</c> comparer becomes the comparer-less call; <c>null</c> for
    /// any other call, which the plain call lowering takes.
    /// </summary>
    internal IrExpression? RewriteUnloweredSpanCall(
        InvocationExpressionSyntax call,
        IMethodSymbol? symbol
    )
    {
        if (!IsMemoryExtensionsCall(symbol) || _context.LowersByDeclarativeMapping is null)
            return null;

        var emittedCount = EmittedArgumentCount(call, symbol);

        var argumentCount = MeansComparerLessOverload(call, symbol)
            ? ComparerLessArguments(call, symbol).Count
            : emittedCount;

        if (!LowersSpanCall(call, symbol, argumentCount))
            return ReportUnloweredSpanCall(call, symbol);

        return argumentCount != emittedCount ? ComparerLessCall(call, symbol) : null;
    }

    private bool LowersSpanCall(
        InvocationExpressionSyntax call,
        IMethodSymbol method,
        int argumentCount
    ) =>
        (!IsStringSearch(method) || HasStringReceiver(call))
        && LowersByTargetMapping(method, argumentCount);

    /// <summary>
    /// Whether <paramref name="method"/> lowers to a JavaScript string
    /// method: a string search by name, or an <c>IndexOf</c> /
    /// <c>LastIndexOf</c> overload that looks for a sequence
    /// (<c>array.IndexOf(otherArray)</c>, <c>span.IndexOf("ab")</c>).
    /// </summary>
    private static bool IsStringSearch(IMethodSymbol method) =>
        StringSearchMethods.Contains(method.Name)
        || (method.Name is "IndexOf" or "LastIndexOf" && SearchesForASequence(method));

    /// <summary>
    /// Whether the value <paramref name="method"/> looks for is a span, read
    /// from the constructed parameter (the first after the receiver).
    /// </summary>
    private static bool SearchesForASequence(IMethodSymbol method) =>
        method.Parameters.ElementAtOrDefault(method.ReducedFrom is null ? 1 : 0)?.Type
            is INamedTypeSymbol value
        && value.GetStableFullName() == ReadOnlySpanTypeName;

    /// <summary>
    /// Whether the receiver of <paramref name="call"/>, before its implicit
    /// conversion to a span, is a string in JavaScript: a <c>string</c> or a
    /// <c>ReadOnlySpan&lt;char&gt;</c>.
    /// </summary>
    private bool HasStringReceiver(InvocationExpressionSyntax call)
    {
        var receiver = (
            _semantic.GetOperation(call) as IInvocationOperation
        )?.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0);

        if (receiver is null)
            return false;

        var value = receiver.Value;

        while (value is IConversionOperation conversion)
            value = conversion.Operand;

        return value.Type switch
        {
            { SpecialType: SpecialType.System_String } => true,
            INamedTypeSymbol { TypeArguments: [{ SpecialType: SpecialType.System_Char }] } span =>
                span.GetStableFullName() == ReadOnlySpanTypeName,
            _ => false,
        };
    }

    /// <summary>
    /// Whether <paramref name="call"/> is a value search whose comparer is
    /// omitted or the <c>null</c> constant, so it means the comparer-less
    /// overload. The plain call lowering already leaves an omitted comparer
    /// out unless a named argument fills it with its default.
    /// </summary>
    private bool MeansComparerLessOverload(InvocationExpressionSyntax call, IMethodSymbol method)
    {
        if (!ValueSearchMethods.Contains(method.Name) || !TakesComparer(method))
            return false;

        return ComparerArgumentOf(call, method) is not { } comparer || IsNullConstant(comparer);
    }

    private static bool TakesComparer(IMethodSymbol method) =>
        method.Parameters.Any(parameter =>
            parameter.Name == LinqOverloadParameters.ComparerParameterName
        );

    /// <summary>
    /// The arguments <paramref name="call"/> writes, but its comparer, in
    /// the order of the parameters they bind to.
    /// </summary>
    private IReadOnlyList<ArgumentSyntax> ComparerLessArguments(
        InvocationExpressionSyntax call,
        IMethodSymbol method
    ) =>
        [
            .. call
                .ArgumentList.Arguments.Select(argument =>
                    (Argument: argument, Parameter: BoundParameterOf(argument, method))
                )
                .Where(bound =>
                    bound.Parameter.Name != LinqOverloadParameters.ComparerParameterName
                )
                .OrderBy(bound => bound.Parameter.Ordinal)
                .Select(bound => bound.Argument),
        ];

    private IrCallExpression ComparerLessCall(InvocationExpressionSyntax call, IMethodSymbol method)
    {
        var arguments = ComparerLessArguments(call, method)
            .Select(argument => ExtractArgument(argument) with { Name = null })
            .ToList();

        return new IrCallExpression(
            Extract(call.Expression),
            arguments,
            Origin: BuildOrigin(method)
        );
    }

    private static IrUnsupportedExpression ReportUnloweredSpanCall(
        InvocationExpressionSyntax call,
        IMethodSymbol method
    )
    {
        ReportUnsupportedFeature(
            MethodNameLocationOf(call),
            $"The span method 'MemoryExtensions.{method.Name}', which C# binds for an array, "
                + "span or string receiver, has no counterpart for this overload; the call "
                + "throws at run time."
        );

        return new IrUnsupportedExpression(UnloweredSpanCallKind);
    }

    private static bool IsMemoryExtensionsCall([NotNullWhen(true)] IMethodSymbol? symbol) =>
        symbol?.ContainingType.GetStableFullName() == MemoryExtensionsTypeName;
}
