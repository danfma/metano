using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The unexported declarations of the <c>private</c> nested types under one
/// module-level <paramref name="owner"/>, collected from every nesting depth (#412) and
/// placed around the owner's own declarations once they are built.
/// </summary>
internal sealed class ModuleLocalDeclarations(INamedTypeSymbol owner, Compilation compilation)
{
    private readonly ModuleLocalPlacement _placement = new(owner, compilation);

    /// <summary>Declared before the owner: its load-time code may construct them.</summary>
    private readonly List<TsTopLevel> _hoisted = [];

    /// <summary>Declared after the owner: their load-time code reads the owner block.</summary>
    private readonly List<TsTopLevel> _trailing = [];

    /// <summary>
    /// The list the declarations of <paramref name="privateType"/> go to, by its
    /// load-time dependencies (<see cref="ModuleLocalPlacement.MustFollowOwner"/>).
    /// </summary>
    public List<TsTopLevel> SinkFor(INamedTypeSymbol privateType) =>
        _placement.MustFollowOwner(privateType) ? _trailing : _hoisted;

    /// <summary>
    /// Inserts the hoisted declarations before the owner's own, which start at
    /// <paramref name="ownerStart"/>, and appends the trailing ones after them.
    /// </summary>
    public void PlaceAround(List<TsTopLevel> statements, int ownerStart)
    {
        statements.InsertRange(ownerStart, _hoisted);
        statements.AddRange(_trailing);
    }
}
