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

    private readonly List<TsVariableDeclaration> _typed = [];

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

        return [.. temporaries.Declarations(), .. mapped];
    }

    /// <summary>
    /// Lowers one expression through <paramref name="map"/> with a fresh scope of
    /// temporaries, for a statement the caller places: the expression and the
    /// declarations of the temporaries it assigns (none when it assigns none).
    /// </summary>
    public static (TsExpression Value, IReadOnlyList<TsStatement> Declarations) AroundExpression(
        IrToTsBridgeContext context,
        Func<IrToTsBridgeContext, TsExpression> map
    )
    {
        var temporaries = new TsTemporaries(context.ReservedLocals);
        var mapped = map(context with { Temporaries = temporaries });

        return (mapped, [.. temporaries.Declarations()]);
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

    /// <summary>
    /// A new temporary named after <paramref name="stem"/> (<c>$r</c>, <c>$r1</c>, …),
    /// declared untyped. A name in <paramref name="alsoReserved"/> (the locals
    /// the lowered loops around the use site reserve) is skipped too.
    /// </summary>
    public TsIdentifier Declare(string stem, IReadOnlySet<string>? alsoReserved = null)
    {
        var name = Allocate(stem, alsoReserved);

        _names.Add(name);

        return new TsIdentifier(name);
    }

    /// <summary>
    /// The temporary <paramref name="name"/> exactly, declared untyped, or
    /// <c>null</c> when that name is taken here.
    /// </summary>
    public TsIdentifier? TryDeclareExactly(string name)
    {
        if (IsTaken(null)(name))
            return null;

        _names.Add(name);

        return new TsIdentifier(name);
    }

    /// <summary>
    /// A new temporary named after <paramref name="stem"/>, declared with
    /// <paramref name="type"/> (<c>let $c: Circle;</c>) so a closure may read it.
    /// </summary>
    public TsIdentifier DeclareTyped(
        string stem,
        TsType type,
        IReadOnlySet<string>? alsoReserved = null
    )
    {
        var name = Allocate(stem, alsoReserved);

        _typed.Add(new TsVariableDeclaration(name, Initializer: null, Const: false, Type: type));

        return new TsIdentifier(name);
    }

    private string Allocate(string stem, IReadOnlySet<string>? alsoReserved)
    {
        var name = FreeName(stem, IsTaken(alsoReserved), _usesByStem.GetValueOrDefault(stem));

        var suffix =
            name.Length == stem.Length
                ? 0
                : int.Parse(name[stem.Length..], CultureInfo.InvariantCulture);

        _usesByStem[stem] = suffix + 1;

        return name;
    }

    /// <summary>
    /// Whether a name is unavailable: reserved by an enclosing loop or the use
    /// site, or already declared here under another stem (<c>$s1</c> as the
    /// second <c>$s</c> and as the temporary of a designator <c>s1</c>).
    /// </summary>
    private Func<string, bool> IsTaken(IReadOnlySet<string>? alsoReserved) =>
        name =>
            _reserved.Contains(name)
            || alsoReserved?.Contains(name) == true
            || _names.Contains(name)
            || _typed.Exists(declaration => declaration.Name == name);

    private static string FreeName(string stem, IReadOnlySet<string> reserved, int start) =>
        FreeName(stem, reserved.Contains, start);

    private static string FreeName(string stem, Func<string, bool> isTaken, int start)
    {
        for (var index = start; ; index++)
        {
            var name = index == 0 ? stem : $"{stem}{index}";

            if (!isTaken(name))
                return name;
        }
    }

    /// <summary>
    /// The head of the statement list: the untyped temporaries in one
    /// <c>let</c>, then each typed one.
    /// </summary>
    private IEnumerable<TsStatement> Declarations()
    {
        if (_names.Count > 0)
            yield return Declaration();

        foreach (var typed in _typed)
            yield return typed;
    }

    private TsRawStatement Declaration() => new($"let {string.Join(", ", _names)};");
}
