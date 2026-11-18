using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The unexported declarations of the nested types declared at module level under one
/// module-level <paramref name="owner"/> — the <c>private</c> ones (#412) and the
/// hoisted ones (spec 011 FR-040) — collected from every nesting depth and placed around
/// the owner's own declarations once they are built. It also records how each exposed
/// hoisted type is reached through its container (<see cref="Expose"/>), which the
/// container's static getters and the owner's <c>declare namespace</c> read.
/// </summary>
internal sealed class ModuleLocalDeclarations(
    INamedTypeSymbol owner,
    Compilation compilation,
    NestedTypeHoisting hoisting
)
{
    private readonly ModuleLocalPlacement _placement = new(owner, compilation, hoisting);

    /// <summary>The declarations of each module-level type, in collection order.</summary>
    private readonly Dictionary<INamedTypeSymbol, List<TsTopLevel>> _declarations = new(
        SymbolEqualityComparer.Default
    );

    /// <summary>Declared before the owner: its load-time code may construct them.</summary>
    private readonly List<INamedTypeSymbol> _hoisted = [];

    /// <summary>Declared after the owner: their load-time code reads the owner block.</summary>
    private readonly List<INamedTypeSymbol> _trailing = [];

    /// <summary>Module-scope companions of nested types, declared first (<see cref="Hoist"/>).</summary>
    private readonly List<TsTopLevel> _hoistedStatements = [];

    private readonly Dictionary<INamedTypeSymbol, List<HoistedNestedType>> _exposed = new(
        SymbolEqualityComparer.Default
    );

    /// <summary>
    /// The list the declarations of the module-level <paramref name="nestedType"/> go
    /// to; its side of the owner follows its load-time dependencies
    /// (<see cref="ModuleLocalPlacement.MustFollowOwner"/>).
    /// </summary>
    public List<TsTopLevel> SinkFor(INamedTypeSymbol nestedType)
    {
        var sink = new List<TsTopLevel>();

        _declarations[nestedType] = sink;
        (_placement.MustFollowOwner(nestedType) ? _trailing : _hoisted).Add(nestedType);

        return sink;
    }

    /// <summary>Records how the hoisted <paramref name="nested"/> is reached through its container.</summary>
    public void Expose(INamedTypeSymbol container, HoistedNestedType nested)
    {
        if (!_exposed.TryGetValue(container, out var members))
            _exposed[container] = members = [];

        members.Add(nested);
    }

    /// <summary>The exposed hoisted types of <paramref name="container"/>, in declaration order.</summary>
    public IReadOnlyList<HoistedNestedType> ExposedIn(INamedTypeSymbol container) =>
        _exposed.TryGetValue(container, out var members) ? members : [];

    /// <summary>
    /// Places <paramref name="statement"/> before the module-level owner's
    /// declarations: a nested type's module-scope companion (the memo of its
    /// runtime type descriptor, spec 011 FR-018).
    /// </summary>
    public void Hoist(TsTopLevel statement) => _hoistedStatements.Add(statement);

    /// <summary>
    /// Inserts the hoisted declarations before the owner's own, which start at
    /// <paramref name="ownerStart"/>, and appends the trailing ones after them.
    /// </summary>
    public void PlaceAround(List<TsTopLevel> statements, int ownerStart)
    {
        statements.InsertRange(ownerStart, [.. _hoistedStatements, .. DeclarationsOf(_hoisted)]);
        statements.AddRange(DeclarationsOf(_trailing));
    }

    private IEnumerable<TsTopLevel> DeclarationsOf(IReadOnlyList<INamedTypeSymbol> side) =>
        _placement.Order(side).SelectMany(type => _declarations[type]);
}

/// <summary>
/// How a hoisted nested type is reached through its container: the getter
/// <c>static get Name()</c> when it has a value (a class, an enum object), the getter
/// <c>static get Name$runtimeType()</c> when it has a descriptor literal, and the
/// alias <c>type Name&lt;T&gt; = Declared&lt;T&gt;</c> of the container's
/// <c>declare namespace</c>.
/// </summary>
/// <param name="Name">The type's own TypeScript name, read as <c>Owner.Name</c>.</param>
/// <param name="DeclaredName">Its module-local identifier (<c>Owner$Name</c>).</param>
/// <param name="Symbol">The nested type, whose own exposed types nest under it.</param>
/// <param name="DescriptorName">Its module-level runtime type descriptor literal
/// (<c>Owner$Name$runtimeType</c>, an enum's or an interface's), if any.</param>
internal sealed record HoistedNestedType(
    string Name,
    string DeclaredName,
    INamedTypeSymbol Symbol,
    bool HasValue,
    IReadOnlyList<TsTypeParameter>? TypeParameters,
    string? DescriptorName
);
