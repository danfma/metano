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

    private readonly Dictionary<string, int> _usesByStem = new(StringComparer.Ordinal);

    /// <summary>
    /// Lowers a statement list through <paramref name="map"/> with a fresh
    /// scope of temporaries, and puts their declaration at its head.
    /// </summary>
    public static IReadOnlyList<TsStatement> Around(
        IrToTsBridgeContext context,
        Func<IrToTsBridgeContext, IReadOnlyList<TsStatement>> map
    )
    {
        var temporaries = new TsTemporaries();
        var mapped = map(context with { Temporaries = temporaries });

        return temporaries._names.Count == 0 ? mapped : [temporaries.Declaration(), .. mapped];
    }

    /// <summary>A new temporary named after <paramref name="stem"/> (<c>$r</c>, <c>$r1</c>, …).</summary>
    public TsIdentifier Declare(string stem)
    {
        var uses = _usesByStem.GetValueOrDefault(stem);
        var name = uses == 0 ? stem : $"{stem}{uses}";

        _usesByStem[stem] = uses + 1;
        _names.Add(name);

        return new TsIdentifier(name);
    }

    private TsRawStatement Declaration() => new($"let {string.Join(", ", _names)};");
}
