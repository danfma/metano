using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Static dispatch table mapping Roslyn LINQ method names to the
/// strongly-typed <see cref="IrLinqOperator"/> enum the IR carries, plus
/// the camelCase identifier the TypeScript bridge emits when lowering
/// the stage to a <c>linq(...)</c> call.
///
/// <para>
/// The table covers <c>System.Linq.Enumerable</c> +
/// <c>System.Linq.Queryable</c> methods with a direct counterpart in
/// the runtime <c>system/linq</c> module. Methods with no pipe
/// equivalent (e.g. <c>Cast&lt;T&gt;</c>, <c>OfType&lt;T&gt;</c>,
/// <c>SequenceEqual</c>) deliberately stay out — the chain walker
/// short-circuits when it hits one, and its call reports MS0001 unless a
/// declarative mapping lowers it (#648).
/// </para>
/// <para>
/// A mapped method is lowered only for the overloads its runtime operator
/// implements (<see cref="RuntimeSignatureOf"/>); the others report MS0001
/// (#648).
/// </para>
/// </summary>
public static class IrLinqMapping
{
    /// <summary>
    /// The position of the queryable tree among the arguments of an
    /// <c>OrDefault</c> terminal: <c>(predicate, defaultValue, queryable)</c>.
    /// </summary>
    public const int OrDefaultQueryableSlot = 2;

    /// <summary>
    /// The name every .NET LINQ overload gives its <c>IEqualityComparer&lt;T&gt;</c>
    /// or <c>IComparer&lt;T&gt;</c> parameter. No runtime operator takes one.
    /// </summary>
    public const string ComparerParameterName = "comparer";

    private static readonly IReadOnlyDictionary<string, IrLinqOperator> _byMethodName =
        new Dictionary<string, IrLinqOperator>(StringComparer.Ordinal)
        {
            // Composition
            ["Where"] = IrLinqOperator.Where,
            ["Select"] = IrLinqOperator.Select,
            ["SelectMany"] = IrLinqOperator.SelectMany,
            ["OrderBy"] = IrLinqOperator.OrderBy,
            ["OrderByDescending"] = IrLinqOperator.OrderByDescending,
            ["ThenBy"] = IrLinqOperator.ThenBy,
            ["ThenByDescending"] = IrLinqOperator.ThenByDescending,
            ["GroupBy"] = IrLinqOperator.GroupBy,
            ["Take"] = IrLinqOperator.Take,
            ["Skip"] = IrLinqOperator.Skip,
            ["TakeWhile"] = IrLinqOperator.TakeWhile,
            ["SkipWhile"] = IrLinqOperator.SkipWhile,
            ["Distinct"] = IrLinqOperator.Distinct,
            ["DistinctBy"] = IrLinqOperator.DistinctBy,
            ["Concat"] = IrLinqOperator.Concat,
            ["Append"] = IrLinqOperator.Append,
            ["Prepend"] = IrLinqOperator.Prepend,
            ["Reverse"] = IrLinqOperator.Reverse,
            ["Zip"] = IrLinqOperator.Zip,

            // Terminals
            ["ToArray"] = IrLinqOperator.ToArray,
            // C# `ToList<T>()` returns `List<T>` but TS uses arrays for
            // both — collapse to `toArray` so the runtime stays minimal.
            ["ToList"] = IrLinqOperator.ToArray,
            ["ToDictionary"] = IrLinqOperator.ToMap,
            ["ToHashSet"] = IrLinqOperator.ToSet,
            ["First"] = IrLinqOperator.First,
            ["FirstOrDefault"] = IrLinqOperator.FirstOrDefault,
            ["Last"] = IrLinqOperator.Last,
            ["LastOrDefault"] = IrLinqOperator.LastOrDefault,
            ["Single"] = IrLinqOperator.Single,
            ["SingleOrDefault"] = IrLinqOperator.SingleOrDefault,
            ["Any"] = IrLinqOperator.Any,
            ["All"] = IrLinqOperator.All,
            ["Count"] = IrLinqOperator.Count,
            // `LongCount` differs from `Count` only in the .NET return
            // type (long vs int); JS has a single number, so the runtime
            // counterpart collapses to `count`.
            ["LongCount"] = IrLinqOperator.Count,
            ["Sum"] = IrLinqOperator.Sum,
            ["Min"] = IrLinqOperator.Min,
            ["Max"] = IrLinqOperator.Max,
            ["MinBy"] = IrLinqOperator.MinBy,
            ["MaxBy"] = IrLinqOperator.MaxBy,
            ["Average"] = IrLinqOperator.Average,
            ["Contains"] = IrLinqOperator.Contains,
            ["Aggregate"] = IrLinqOperator.Aggregate,
        };

    private static readonly IReadOnlyDictionary<IrLinqOperator, string> _emittedNames =
        new Dictionary<IrLinqOperator, string>
        {
            [IrLinqOperator.Where] = "where",
            [IrLinqOperator.Select] = "select",
            [IrLinqOperator.SelectMany] = "selectMany",
            [IrLinqOperator.OrderBy] = "orderBy",
            [IrLinqOperator.OrderByDescending] = "orderByDescending",
            [IrLinqOperator.ThenBy] = "thenBy",
            [IrLinqOperator.ThenByDescending] = "thenByDescending",
            [IrLinqOperator.GroupBy] = "groupBy",
            [IrLinqOperator.Take] = "take",
            [IrLinqOperator.Skip] = "skip",
            [IrLinqOperator.TakeWhile] = "takeWhile",
            [IrLinqOperator.SkipWhile] = "skipWhile",
            [IrLinqOperator.Distinct] = "distinct",
            [IrLinqOperator.DistinctBy] = "distinctBy",
            [IrLinqOperator.Concat] = "concat",
            [IrLinqOperator.Append] = "append",
            [IrLinqOperator.Prepend] = "prepend",
            [IrLinqOperator.Reverse] = "reverse",
            [IrLinqOperator.Zip] = "zip",
            [IrLinqOperator.ToArray] = "toArray",
            [IrLinqOperator.ToMap] = "toMap",
            [IrLinqOperator.ToSet] = "toSet",
            [IrLinqOperator.First] = "first",
            [IrLinqOperator.FirstOrDefault] = "firstOrDefault",
            [IrLinqOperator.Last] = "last",
            [IrLinqOperator.LastOrDefault] = "lastOrDefault",
            [IrLinqOperator.Single] = "single",
            [IrLinqOperator.SingleOrDefault] = "singleOrDefault",
            [IrLinqOperator.Any] = "any",
            [IrLinqOperator.All] = "all",
            [IrLinqOperator.Count] = "count",
            [IrLinqOperator.Sum] = "sum",
            [IrLinqOperator.Min] = "min",
            [IrLinqOperator.Max] = "max",
            [IrLinqOperator.MinBy] = "minBy",
            [IrLinqOperator.MaxBy] = "maxBy",
            [IrLinqOperator.Average] = "average",
            [IrLinqOperator.Contains] = "contains",
            [IrLinqOperator.Aggregate] = "aggregate",
        };

    // The overloads each runtime operator in `system/linq` implements, each
    // named by its .NET parameters after the source ("" for none);
    // `Queryable` names a few differently (`source2`, `item`). The first
    // value is the position of the operator's `QueryableMeta` parameter.
    // `LinqRuntimeSignatureTests` checks both against the runtime sources.
    private static readonly IReadOnlyDictionary<
        IrLinqOperator,
        IrLinqRuntimeSignature
    > _runtimeSignatures = new Dictionary<IrLinqOperator, IrLinqRuntimeSignature>
    {
        [IrLinqOperator.Where] = IrLinqRuntimeSignature.Of(1, "predicate"),
        [IrLinqOperator.Select] = IrLinqRuntimeSignature.Of(1, "selector"),
        [IrLinqOperator.SelectMany] = IrLinqRuntimeSignature.Of(1, "selector"),
        [IrLinqOperator.OrderBy] = IrLinqRuntimeSignature.Of(1, "keySelector"),
        [IrLinqOperator.OrderByDescending] = IrLinqRuntimeSignature.Of(1, "keySelector"),
        [IrLinqOperator.ThenBy] = IrLinqRuntimeSignature.Of(1, "keySelector"),
        [IrLinqOperator.ThenByDescending] = IrLinqRuntimeSignature.Of(1, "keySelector"),
        [IrLinqOperator.GroupBy] = IrLinqRuntimeSignature.Of(1, "keySelector"),
        [IrLinqOperator.Take] = IrLinqRuntimeSignature.Of(null, "count"),
        [IrLinqOperator.Skip] = IrLinqRuntimeSignature.Of(null, "count"),
        [IrLinqOperator.TakeWhile] = IrLinqRuntimeSignature.Of(1, "predicate"),
        [IrLinqOperator.SkipWhile] = IrLinqRuntimeSignature.Of(1, "predicate"),
        [IrLinqOperator.Distinct] = IrLinqRuntimeSignature.Of(null, ""),
        [IrLinqOperator.DistinctBy] = IrLinqRuntimeSignature.Of(1, "keySelector"),
        [IrLinqOperator.Concat] = IrLinqRuntimeSignature.Of(null, "second", "source2"),
        [IrLinqOperator.Append] = IrLinqRuntimeSignature.Of(null, "element"),
        [IrLinqOperator.Prepend] = IrLinqRuntimeSignature.Of(null, "element"),
        [IrLinqOperator.Reverse] = IrLinqRuntimeSignature.Of(null, ""),
        [IrLinqOperator.Zip] = IrLinqRuntimeSignature.Of(
            2,
            "second, resultSelector",
            "source2, resultSelector"
        ),
        [IrLinqOperator.ToArray] = IrLinqRuntimeSignature.Of(null, ""),
        [IrLinqOperator.ToMap] = IrLinqRuntimeSignature.Of(
            2,
            "keySelector",
            "keySelector, elementSelector"
        ),
        [IrLinqOperator.ToSet] = IrLinqRuntimeSignature.Of(null, ""),
        [IrLinqOperator.First] = IrLinqRuntimeSignature.Of(1, "", "predicate"),
        [IrLinqOperator.FirstOrDefault] = IrLinqRuntimeSignature.Of(
            OrDefaultQueryableSlot,
            "",
            "predicate",
            "defaultValue",
            "predicate, defaultValue"
        ),
        [IrLinqOperator.Last] = IrLinqRuntimeSignature.Of(1, "", "predicate"),
        [IrLinqOperator.LastOrDefault] = IrLinqRuntimeSignature.Of(
            OrDefaultQueryableSlot,
            "",
            "predicate",
            "defaultValue",
            "predicate, defaultValue"
        ),
        [IrLinqOperator.Single] = IrLinqRuntimeSignature.Of(1, "", "predicate"),
        [IrLinqOperator.SingleOrDefault] = IrLinqRuntimeSignature.Of(
            OrDefaultQueryableSlot,
            "",
            "predicate",
            "defaultValue",
            "predicate, defaultValue"
        ),
        [IrLinqOperator.Any] = IrLinqRuntimeSignature.Of(1, "", "predicate"),
        [IrLinqOperator.All] = IrLinqRuntimeSignature.Of(1, "predicate"),
        [IrLinqOperator.Count] = IrLinqRuntimeSignature.Of(1, "", "predicate"),
        [IrLinqOperator.Sum] = IrLinqRuntimeSignature.Of(1, "", "selector"),
        [IrLinqOperator.Min] = IrLinqRuntimeSignature.Of(1, "", "selector"),
        [IrLinqOperator.Max] = IrLinqRuntimeSignature.Of(1, "", "selector"),
        [IrLinqOperator.MinBy] = IrLinqRuntimeSignature.Of(1, "keySelector"),
        [IrLinqOperator.MaxBy] = IrLinqRuntimeSignature.Of(1, "keySelector"),
        [IrLinqOperator.Average] = IrLinqRuntimeSignature.Of(1, "", "selector"),
        [IrLinqOperator.Contains] = IrLinqRuntimeSignature.Of(null, "value", "item"),
        [IrLinqOperator.Aggregate] = IrLinqRuntimeSignature.Of(null, "seed, func"),
    };

    /// <summary>
    /// Returns true when <paramref name="symbol"/> is a method on
    /// <c>System.Linq.Enumerable</c> or <c>System.Linq.Queryable</c>
    /// whose name has a pipe-runtime counterpart.
    /// </summary>
    public static bool TryResolve(IMethodSymbol? symbol, out IrLinqOperator op)
    {
        op = default;
        if (symbol is null)
            return false;
        if (!IsLinqContainer(symbol.ContainingType))
            return false;
        return _byMethodName.TryGetValue(symbol.Name, out op);
    }

    /// <summary>Camel-case identifier the TypeScript bridge emits.</summary>
    public static string EmittedName(IrLinqOperator op) => _emittedNames[op];

    /// <summary>Whether the operator is a chain terminal (eager).</summary>
    public static bool IsTerminal(IrLinqOperator op) =>
        op
            is IrLinqOperator.ToArray
                or IrLinqOperator.ToMap
                or IrLinqOperator.ToSet
                or IrLinqOperator.First
                or IrLinqOperator.FirstOrDefault
                or IrLinqOperator.Last
                or IrLinqOperator.LastOrDefault
                or IrLinqOperator.Single
                or IrLinqOperator.SingleOrDefault
                or IrLinqOperator.Any
                or IrLinqOperator.All
                or IrLinqOperator.Count
                or IrLinqOperator.Sum
                or IrLinqOperator.Min
                or IrLinqOperator.Max
                or IrLinqOperator.MinBy
                or IrLinqOperator.MaxBy
                or IrLinqOperator.Average
                or IrLinqOperator.Contains
                or IrLinqOperator.Aggregate;

    /// <summary>
    /// Whether the terminal returns a default value when no element
    /// qualifies. Its runtime counterpart takes
    /// <c>(predicate, defaultValue, queryable)</c>.
    /// </summary>
    public static bool IsOrDefaultTerminal(IrLinqOperator op) =>
        op
            is IrLinqOperator.FirstOrDefault
                or IrLinqOperator.LastOrDefault
                or IrLinqOperator.SingleOrDefault;

    /// <summary>
    /// The overloads the runtime operator of <paramref name="op"/> implements
    /// and where it reads a queryable tree.
    /// </summary>
    public static IrLinqRuntimeSignature RuntimeSignatureOf(IrLinqOperator op) =>
        _runtimeSignatures[op];

    /// <summary>The .NET method names that resolve to <paramref name="op"/>.</summary>
    internal static IEnumerable<string> MethodNamesOf(IrLinqOperator op) =>
        _byMethodName.Where(entry => entry.Value == op).Select(entry => entry.Key);

    /// <summary>
    /// Whether the runtime operator of <paramref name="op"/> implements the
    /// overload <paramref name="method"/> binds to, or implements it only
    /// without its comparer.
    /// </summary>
    public static IrLinqOverloadSupport OverloadSupportOf(IMethodSymbol method, IrLinqOperator op)
    {
        var signature = _runtimeSignatures[op];
        var parameters = ParameterNamesOf(method);

        if (signature.Implements(parameters))
            return IrLinqOverloadSupport.Implemented;

        var withoutComparer = parameters.Where(name => name != ComparerParameterName).ToList();

        var passesComparer = withoutComparer.Count < parameters.Count;

        return passesComparer && signature.Implements(withoutComparer)
            ? IrLinqOverloadSupport.IgnoredComparer
            : IrLinqOverloadSupport.Unimplemented;
    }

    /// <summary>
    /// Whether the runtime operator of <paramref name="op"/> reads the
    /// queryable tree the stage passes after its
    /// <paramref name="argumentCount"/> arguments. An <c>OrDefault</c>
    /// terminal pads the arguments it omits up to its slot.
    /// </summary>
    public static bool ReadsQueryableTreeAfter(IrLinqOperator op, int argumentCount)
    {
        if (_runtimeSignatures[op].QueryableSlot is not { } slot)
            return false;

        return IsOrDefaultTerminal(op) ? argumentCount <= slot : argumentCount == slot;
    }

    /// <summary>
    /// The parameter names of <paramref name="method"/> after the source, as
    /// the runtime signatures name an overload.
    /// </summary>
    public static IReadOnlyList<string> ParameterNamesOf(IMethodSymbol method) =>
        [.. (method.ReducedFrom ?? method).Parameters.Skip(1).Select(parameter => parameter.Name)];

    /// <summary>
    /// Whether <paramref name="symbol"/> is a method of
    /// <c>System.Linq.Enumerable</c> or <c>System.Linq.Queryable</c>.
    /// </summary>
    public static bool IsLinqMethod([NotNullWhen(true)] IMethodSymbol? symbol) =>
        symbol is not null && IsLinqContainer(symbol.ContainingType);

    private static bool IsLinqContainer(INamedTypeSymbol? type)
    {
        if (type is null)
            return false;
        var fullName = type.ToDisplayString();
        return fullName is "System.Linq.Enumerable" or "System.Linq.Queryable";
    }
}
