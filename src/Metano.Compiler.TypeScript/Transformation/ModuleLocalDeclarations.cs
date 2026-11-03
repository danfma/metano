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
    public List<TsTopLevel> Hoisted { get; } = [];

    /// <summary>Declared after the owner: their load-time code reads the owner block.</summary>
    public List<TsTopLevel> Trailing { get; } = [];

    /// <inheritdoc cref="ModuleLocalPlacement.MustFollowOwner"/>
    public bool MustFollowOwner(INamedTypeSymbol privateType) =>
        _placement.MustFollowOwner(privateType);

    /// <summary>
    /// Inserts <see cref="Hoisted"/> before the owner's declarations, which start at
    /// <paramref name="ownerStart"/>, and appends <see cref="Trailing"/> after them.
    /// </summary>
    public void PlaceAround(List<TsTopLevel> statements, int ownerStart)
    {
        statements.InsertRange(ownerStart, Hoisted);
        statements.AddRange(Trailing);
    }
}
