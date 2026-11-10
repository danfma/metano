using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using static Metano.Compiler.Extraction.LinqOverloadParameters;

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
    // named by its .NET parameters after the source; `Queryable` names a few
    // differently (`source2`, `item`). The first value is the position of
    // the operator's `QueryableMeta` parameter. `LinqRuntimeSignatureTests`
    // checks that each declared overload exists in .NET, that the runtime
    // operator takes as many arguments as it passes and reads its tree at that
    // slot, and that the tsgo-checked golden `linq-supported-overloads.ts`
    // calls every declared overload: names and positions, not types.
    private static readonly IReadOnlyDictionary<
        IrLinqOperator,
        IrLinqRuntimeSignature
    > _runtimeSignatures = new Dictionary<IrLinqOperator, IrLinqRuntimeSignature>
    {
        [IrLinqOperator.Where] = IrLinqRuntimeSignature.Of(1, Named("predicate")),
        [IrLinqOperator.Select] = IrLinqRuntimeSignature.Of(1, Named("selector")),
        [IrLinqOperator.SelectMany] = IrLinqRuntimeSignature.Of(1, Named("selector")),
        [IrLinqOperator.OrderBy] = IrLinqRuntimeSignature.Of(1, Named("keySelector")),
        [IrLinqOperator.OrderByDescending] = IrLinqRuntimeSignature.Of(1, Named("keySelector")),
        [IrLinqOperator.ThenBy] = IrLinqRuntimeSignature.Of(1, Named("keySelector")),
        [IrLinqOperator.ThenByDescending] = IrLinqRuntimeSignature.Of(1, Named("keySelector")),
        [IrLinqOperator.GroupBy] = IrLinqRuntimeSignature.Of(1, Named("keySelector")),
        [IrLinqOperator.Take] = IrLinqRuntimeSignature.Of(null, Named("count")),
        [IrLinqOperator.Skip] = IrLinqRuntimeSignature.Of(null, Named("count")),
        [IrLinqOperator.TakeWhile] = IrLinqRuntimeSignature.Of(1, Named("predicate")),
        [IrLinqOperator.SkipWhile] = IrLinqRuntimeSignature.Of(1, Named("predicate")),
        [IrLinqOperator.Distinct] = IrLinqRuntimeSignature.Of(null, Named()),
        [IrLinqOperator.DistinctBy] = IrLinqRuntimeSignature.Of(1, Named("keySelector")),
        [IrLinqOperator.Concat] = IrLinqRuntimeSignature.Of(
            null,
            Named("second"),
            Named("source2")
        ),
        [IrLinqOperator.Append] = IrLinqRuntimeSignature.Of(null, Named("element")),
        [IrLinqOperator.Prepend] = IrLinqRuntimeSignature.Of(null, Named("element")),
        [IrLinqOperator.Reverse] = IrLinqRuntimeSignature.Of(null, Named()),
        [IrLinqOperator.Zip] = IrLinqRuntimeSignature.Of(
            2,
            Named("second", "resultSelector"),
            Named("source2", "resultSelector")
        ),
        [IrLinqOperator.ToArray] = IrLinqRuntimeSignature.Of(null, Named()),
        [IrLinqOperator.ToMap] = IrLinqRuntimeSignature.Of(
            2,
            Named("keySelector"),
            Named("keySelector", "elementSelector")
        ),
        [IrLinqOperator.ToSet] = IrLinqRuntimeSignature.Of(null, Named()),
        [IrLinqOperator.First] = IrLinqRuntimeSignature.Of(1, Named(), Named("predicate")),
        [IrLinqOperator.FirstOrDefault] = OrDefaultSignature,
        [IrLinqOperator.Last] = IrLinqRuntimeSignature.Of(1, Named(), Named("predicate")),
        [IrLinqOperator.LastOrDefault] = OrDefaultSignature,
        [IrLinqOperator.Single] = IrLinqRuntimeSignature.Of(1, Named(), Named("predicate")),
        [IrLinqOperator.SingleOrDefault] = OrDefaultSignature,
        [IrLinqOperator.Any] = IrLinqRuntimeSignature.Of(1, Named(), Named("predicate")),
        [IrLinqOperator.All] = IrLinqRuntimeSignature.Of(1, Named("predicate")),
        [IrLinqOperator.Count] = IrLinqRuntimeSignature.Of(1, Named(), Named("predicate")),
        [IrLinqOperator.Sum] = IrLinqRuntimeSignature.Of(1, Named(), Named("selector")),
        [IrLinqOperator.Min] = IrLinqRuntimeSignature.Of(1, Named(), Named("selector")),
        [IrLinqOperator.Max] = IrLinqRuntimeSignature.Of(1, Named(), Named("selector")),
        [IrLinqOperator.MinBy] = IrLinqRuntimeSignature.Of(1, Named("keySelector")),
        [IrLinqOperator.MaxBy] = IrLinqRuntimeSignature.Of(1, Named("keySelector")),
        [IrLinqOperator.Average] = IrLinqRuntimeSignature.Of(1, Named(), Named("selector")),
        [IrLinqOperator.Contains] = IrLinqRuntimeSignature.Of(null, Named("value"), Named("item")),
        [IrLinqOperator.Aggregate] = IrLinqRuntimeSignature.Of(null, Named("seed", "func")),
    };

    /// <summary>
    /// The <c>OrDefault</c> terminals: <c>(predicate, defaultValue, queryable)</c>,
    /// padding the arguments a stage omits before the tree.
    /// </summary>
    private static IrLinqRuntimeSignature OrDefaultSignature =>
        IrLinqRuntimeSignature
            .Of(
                2,
                Named(),
                Named("predicate"),
                Named("defaultValue"),
                Named("predicate", "defaultValue")
            )
            .PaddingOmittedArguments();

    /// <summary>
    /// Returns true when <paramref name="symbol"/> is a method on
    /// <c>System.Linq.Enumerable</c> or <c>System.Linq.Queryable</c>
    /// whose name has a pipe-runtime counterpart.
    /// </summary>
    public static bool TryResolve([NotNullWhen(true)] IMethodSymbol? symbol, out IrLinqOperator op)
    {
        op = default;

        return IsLinqMethod(symbol) && _byMethodName.TryGetValue(symbol.Name, out op);
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
        _runtimeSignatures.TryGetValue(op, out var signature)
            ? signature
            : throw new InvalidOperationException(
                $"No runtime signature is declared for the LINQ operator '{op}'."
            );

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
        var signature = RuntimeSignatureOf(op);
        var overload = LinqOverloadParameters.From(method);

        if (signature.Implements(overload))
            return IrLinqOverloadSupport.Implemented;

        return overload.PassesComparer && signature.Implements(overload.WithoutComparer())
            ? IrLinqOverloadSupport.IgnoredComparer
            : IrLinqOverloadSupport.Unimplemented;
    }

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
