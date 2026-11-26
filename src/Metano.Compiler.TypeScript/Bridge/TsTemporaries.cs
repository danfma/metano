using System.Globalization;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The temporaries one statement list declares at its head (<c>let $r, $i;</c>)
/// for the expressions in it that assign them inline, in a comma expression
/// at their original position (spec 011 contracts/runtime-collections.md §
/// Indexer compound assignments): each site takes distinct names, so a nested
/// site never shares a temporary with an enclosing one (<c>$r</c>, <c>$r1</c>;
/// <c>$arg0</c>, <c>$arg0_1</c> for a stem that ends in a digit). A C#
/// identifier cannot contain <c>$</c>, so no temporary collides with a user name.
/// </summary>
internal sealed class TsTemporaries
{
    private readonly List<string> _names = [];

    private readonly List<TsVariableDeclaration> _typed = [];

    private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

    private readonly Dictionary<(object Node, string Role), TsIdentifier> _temporariesByNode = new(
        NodeRoleComparer.Instance
    );

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
        var (mapped, declarations) = Around<IReadOnlyList<TsStatement>>(context, map);

        return [.. declarations, .. mapped];
    }

    /// <summary>
    /// Lowers through <paramref name="map"/> with a fresh scope of temporaries:
    /// what it lowered, and the declarations of the temporaries it assigns, which
    /// the caller places ahead of it.
    /// </summary>
    public static (T Result, IReadOnlyList<TsStatement> Declarations) Around<T>(
        IrToTsBridgeContext context,
        Func<IrToTsBridgeContext, T> map
    )
    {
        var temporaries = new TsTemporaries(context.ReservedLocals);
        var result = map(context with { Temporaries = temporaries });

        return (result, [.. temporaries.Declarations()]);
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
    /// The temporary of the lowering of <paramref name="node"/> (one per
    /// <paramref name="role"/> when it binds several): made by
    /// <paramref name="make"/> (declared or claimed) the first time, the same one
    /// when the node is lowered again (a lowering that tries one form, drops it and
    /// maps the node once more), so no temporary is declared that nothing assigns.
    /// </summary>
    public TsIdentifier TemporaryFor(object node, Func<TsIdentifier> make, string role = "") =>
        _temporariesByNode.TryGetValue((node, role), out var temporary)
            ? temporary
            : _temporariesByNode[(node, role)] = make();

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
    /// Claims the name <paramref name="name"/> exactly for a binding the statement
    /// list declares itself, in place (<c>const name = value;</c>), so no temporary
    /// takes it; <c>false</c> when that name is taken here.
    /// </summary>
    public bool TryClaimExactly(string name)
    {
        if (IsTaken(null)(name))
            return false;

        _claimed.Add(name);

        return true;
    }

    /// <summary>
    /// A new name after <paramref name="stem"/> that no temporary of this list
    /// takes, not declared yet: a lowering names its binding before it knows
    /// whether it assigns it (<see cref="DeclareClaimed"/>) or drops it.
    /// </summary>
    public TsIdentifier Claim(string stem, IReadOnlySet<string>? alsoReserved = null)
    {
        var name = Allocate(stem, alsoReserved);

        _claimed.Add(name);

        return new TsIdentifier(name);
    }

    /// <summary>Whether <paramref name="name"/> is claimed here and not declared yet.</summary>
    public bool HoldsClaim(string name) => _claimed.Contains(name);

    /// <summary>
    /// Declares a name <see cref="Claim"/> or <see cref="TryClaimExactly"/>
    /// returned, with <paramref name="type"/> when one is given; a name declared
    /// already stays as it is.
    /// </summary>
    public void DeclareClaimed(TsIdentifier claimed, TsType? type = null)
    {
        if (!_claimed.Remove(claimed.Name))
            return;

        if (type is null)
            _names.Add(claimed.Name);
        else
            _typed.Add(
                new TsVariableDeclaration(claimed.Name, Initializer: null, Const: false, Type: type)
            );
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
                : int.Parse(name[stem.Length..].TrimStart('_'), CultureInfo.InvariantCulture);

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
            || _claimed.Contains(name)
            || _typed.Exists(declaration => declaration.Name == name);

    private static string FreeName(string stem, IReadOnlySet<string> reserved, int start) =>
        FreeName(stem, reserved.Contains, start);

    private static string FreeName(string stem, Func<string, bool> isTaken, int start)
    {
        for (var index = start; ; index++)
        {
            var name =
                index == 0 ? stem
                : char.IsAsciiDigit(stem[^1]) ? $"{stem}_{index}"
                : $"{stem}{index}";

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

/// <summary>Compares an IR node by identity and its role by ordinal text.</summary>
file sealed class NodeRoleComparer : IEqualityComparer<(object Node, string Role)>
{
    public static readonly NodeRoleComparer Instance = new();

    public bool Equals((object Node, string Role) x, (object Node, string Role) y) =>
        ReferenceEquals(x.Node, y.Node) && string.Equals(x.Role, y.Role, StringComparison.Ordinal);

    public int GetHashCode((object Node, string Role) key) =>
        HashCode.Combine(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Node),
            StringComparer.Ordinal.GetHashCode(key.Role)
        );
}
