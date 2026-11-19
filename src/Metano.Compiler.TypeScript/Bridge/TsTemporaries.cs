using System.Globalization;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The temporaries one statement list declares at its head (<c>let $r, $i;</c>)
/// for the expressions in it that assign them inline, in a comma expression
/// at their original position (spec 011 contracts/runtime-collections.md §
/// Indexer compound assignments): each site takes distinct names, so a nested
/// site never shares a temporary with an enclosing one. A C# identifier cannot
/// contain <c>$</c>, so no temporary collides with a user name.
/// </summary>
internal sealed class TsTemporaries
{
    private readonly List<string> _names = [];

    private readonly System.Collections.Immutable.ImmutableHashSet<string> _reserved;

    private readonly Dictionary<string, int> _usesByStem = new(StringComparer.Ordinal);

    private TsTemporaries(System.Collections.Immutable.ImmutableHashSet<string> reserved) =>
        _reserved = reserved;

    /// <summary>
    /// Lowers a statement list through <paramref name="map"/> with a fresh
    /// scope of temporaries, and puts their declaration at its head.
    /// </summary>
    public static IReadOnlyList<TsStatement> Around(
        IrToTsBridgeContext context,
        Func<IrToTsBridgeContext, IReadOnlyList<TsStatement>> map
    )
    {
        var temporaries = new TsTemporaries(context.ReservedLocals);
        var mapped = map(context with { Temporaries = temporaries });

        return temporaries._names.Count == 0 ? mapped : [temporaries.Declaration(), .. mapped];
    }

    /// <summary>
    /// The block-scoped locals of a lowered loop, one per stem, each the first
    /// of <c>stem</c>, <c>stem1</c>, … that no enclosing loop reserved, and
    /// the context of the loop's body, which reserves them in turn.
    /// </summary>
    public static IReadOnlyList<string> Reserve(
        IrToTsBridgeContext context,
        IReadOnlyList<string> stems,
        out IrToTsBridgeContext bodyContext
    )
    {
        var names = stems.Select(stem => FreeName(stem, context.ReservedLocals, start: 0)).ToList();

        bodyContext = context with { ReservedLocals = context.ReservedLocals.Union(names) };

        return names;
    }

    /// <summary>A new temporary named after <paramref name="stem"/> (<c>$r</c>, <c>$r1</c>, …).</summary>
    public TsIdentifier Declare(string stem)
    {
        var name = FreeName(stem, _reserved, _usesByStem.GetValueOrDefault(stem));

        var suffix =
            name.Length == stem.Length
                ? 0
                : int.Parse(name[stem.Length..], CultureInfo.InvariantCulture);

        _usesByStem[stem] = suffix + 1;
        _names.Add(name);

        return new TsIdentifier(name);
    }

    private static string FreeName(string stem, IReadOnlySet<string> reserved, int start)
    {
        for (var index = start; ; index++)
        {
            var name = index == 0 ? stem : $"{stem}{index}";

            if (!reserved.Contains(name))
                return name;
        }
    }

    private TsRawStatement Declaration() => new($"let {string.Join(", ", _names)};");
}
