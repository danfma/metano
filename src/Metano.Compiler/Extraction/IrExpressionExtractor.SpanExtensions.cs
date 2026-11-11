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
/// LINQ call does. Three rules sit beside the mappings:
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
///     A comparer that is omitted or the <c>null</c> constant asks for the
///     default comparison: the call lowers as the comparer-less overload,
///     without the comparer argument (as a LINQ stage does, #648).
///   </item>
///   <item>
///     A lowered value search over a tuple element reports that the tuples
///     compare by reference, as a <c>List&lt;(int, int)&gt;</c> lookup does;
///     a call the span rule reports is reported once.
///   </item>
/// </list>
/// A target without a mappings port (Dart) keeps every call as it is.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string MemoryExtensionsTypeName = "System.MemoryExtensions";

    private const string UnloweredSpanCallKind = "UnloweredSpanCall";

    private const string ReadOnlySpanTypeName = "System.ReadOnlySpan<T>";

    private const string SpanTypeName = "System.Span<T>";

    // TODO(#657): "a char span over a string is a JavaScript string, any
    // other span is a JavaScript array" is a fact of the TypeScript
    // representation. The receiver rule below (StringSearchMethods,
    // IsStringSearch, HasStringReceiver) must become a declarative receiver
    // filter on [MapMethod], answered by the target through the mappings port.
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
    /// lowers reports and becomes the placeholder, and a call with a
    /// <c>null</c> comparer becomes the comparer-less call; <c>null</c> for
    /// any other call, which the plain call lowering takes.
    /// </summary>
    internal IrExpression? RewriteUnloweredSpanCall(
        InvocationExpressionSyntax call,
        IMethodSymbol? symbol
    )
    {
        // TODO(#549): the presence of the mappings port stands for "this
        // target lowers MemoryExtensions calls"; it should be a capability
        // of its own.
        if (!IsMemoryExtensionsCall(symbol) || _context.LowersByDeclarativeMapping is null)
            return null;

        var emittedCount = EmittedArgumentCount(call, symbol);

        var comparerLess = MeansComparerLessOverload(call, symbol)
            ? ComparerLessArguments(call, symbol)
            : null;

        var argumentCount = comparerLess?.Count ?? emittedCount;
        var dropsComparerArgument = argumentCount != emittedCount;

        if (!LowersSpanCall(call, symbol, argumentCount))
            return ReportUnloweredSpanCall(call, symbol);

        if (ComparesTupleElementInSpan(symbol))
            ReportTupleComparison(call, symbol);

        return dropsComparerArgument ? ComparerLessCall(call, symbol, comparerLess!) : null;
    }

    private bool LowersSpanCall(
        InvocationExpressionSyntax call,
        IMethodSymbol method,
        int argumentCount
    ) => LowersOnItsReceiver(call, method) && LowersByTargetMapping(method, argumentCount);

    /// <summary>
    /// Whether <paramref name="call"/> is not a string search, or is one on a
    /// receiver that is a string in JavaScript.
    /// </summary>
    private bool LowersOnItsReceiver(InvocationExpressionSyntax call, IMethodSymbol method) =>
        !IsStringSearch(method) || HasStringReceiver(call);

    /// <summary>
    /// Whether <paramref name="method"/> lowers to a JavaScript string
    /// method: a string search by name, or a subsequence search.
    /// </summary>
    private static bool IsStringSearch(IMethodSymbol method) =>
        StringSearchMethods.Contains(method.Name) || IsSubsequenceSearch(method);

    /// <summary>
    /// Whether <paramref name="method"/> is an <c>IndexOf</c> /
    /// <c>LastIndexOf</c> overload that looks for a sequence
    /// (<c>array.IndexOf(otherArray)</c>, <c>span.IndexOf("ab")</c>).
    /// </summary>
    private static bool IsSubsequenceSearch(IMethodSymbol method) =>
        method.Name is "IndexOf" or "LastIndexOf"
        && SearchedValueOf(method)?.Type is INamedTypeSymbol value
        && value.GetStableFullName() == ReadOnlySpanTypeName;

    /// <summary>
    /// The constructed parameter that holds the searched value: the first
    /// after the receiver, which a static-form call passes as its first
    /// argument.
    /// </summary>
    private static IParameterSymbol? SearchedValueOf(IMethodSymbol method) =>
        method.Parameters.ElementAtOrDefault(method.ReducedFrom is null ? 1 : 0);

    /// <summary>
    /// Whether the receiver of <paramref name="call"/>, as written before the
    /// implicit conversion to a span, is a string in JavaScript: a
    /// <c>string</c> or a <c>ReadOnlySpan&lt;char&gt;</c>.
    /// </summary>
    private bool HasStringReceiver(InvocationExpressionSyntax call) =>
        ReceiverOf(call) is { } receiver
        && WithoutSpanConversion(receiver.Value).Type switch
        {
            { SpecialType: SpecialType.System_String } => true,
            INamedTypeSymbol { TypeArguments: [{ SpecialType: SpecialType.System_Char }] } span =>
                span.GetStableFullName() == ReadOnlySpanTypeName,
            _ => false,
        };

    private IArgumentOperation? ReceiverOf(InvocationExpressionSyntax call) =>
        (_semantic.GetOperation(call) as IInvocationOperation)?.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Ordinal == 0
        );

    /// <summary>
    /// <paramref name="value"/> without the implicit conversion to a span
    /// that C# 14 applies to an extension receiver; an explicit cast
    /// (<c>(string)o</c>) is kept.
    /// </summary>
    private static IOperation WithoutSpanConversion(IOperation value) =>
        value is IConversionOperation { IsImplicit: true, Type: INamedTypeSymbol target } conversion
        && target.GetStableFullName() is ReadOnlySpanTypeName or SpanTypeName
            ? conversion.Operand
            : value;

    /// <summary>
    /// Whether <paramref name="call"/> passes a comparer that is omitted or
    /// the <c>null</c> constant, so it means the comparer-less overload.
    /// TODO(#549): dropping the comparer follows the TypeScript runtime
    /// representation; the port presence gates it in
    /// <see cref="RewriteUnloweredSpanCall"/>.
    /// </summary>
    private bool MeansComparerLessOverload(InvocationExpressionSyntax call, IMethodSymbol method)
    {
        if (!TakesComparer(method))
            return false;

        return ComparerArgumentOf(call, method) is not { } comparer || IsNullConstant(comparer);
    }

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
                .Where(bound => bound.Parameter.Name != ComparerParameterName)
                .OrderBy(bound => bound.Parameter.Ordinal)
                .Select(bound => bound.Argument),
        ];

    private IrCallExpression ComparerLessCall(
        InvocationExpressionSyntax call,
        IMethodSymbol method,
        IReadOnlyList<ArgumentSyntax> comparerLess
    )
    {
        var arguments = comparerLess
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

    /// <summary>
    /// The value searches of <c>System.MemoryExtensions</c> that C# 14 binds
    /// for an array (<c>pairs.Contains((3, 4))</c>), over a tuple element.
    /// </summary>
    private static bool ComparesTupleElementInSpan(IMethodSymbol method) =>
        ValueSearchMethods.Contains(method.Name)
        && method.TypeArguments is [INamedTypeSymbol { IsTupleType: true }, ..];

    private static bool IsMemoryExtensionsCall([NotNullWhen(true)] IMethodSymbol? symbol) =>
        symbol?.ContainingType.GetStableFullName() == MemoryExtensionsTypeName;
}
