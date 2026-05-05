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
/// the runtime <c>system/linq-pipe</c> module. Methods with no pipe
/// equivalent (e.g. <c>Cast&lt;T&gt;</c>, <c>OfType&lt;T&gt;</c>,
/// <c>SequenceEqual</c>) deliberately stay out — the chain walker
/// short-circuits when it hits one and the legacy fluent emission path
/// continues to handle them.
/// </para>
/// </summary>
public static class IrLinqMapping
{
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

    private static bool IsLinqContainer(INamedTypeSymbol? type)
    {
        if (type is null)
            return false;
        var fullName = type.ToDisplayString();
        return fullName is "System.Linq.Enumerable" or "System.Linq.Queryable";
    }
}
