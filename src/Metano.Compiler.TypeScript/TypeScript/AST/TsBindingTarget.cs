namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// What one slot of a destructuring binding binds: a name or a nested
/// pattern (<c>[a, [b, c]]</c>, <c>{ x: a, y: b }</c>).
/// </summary>
public abstract record TsBindingTarget
{
    /// <summary>The names the target binds, nested ones included, in order.</summary>
    public abstract IEnumerable<string> BoundNames { get; }
}

/// <summary>A bound name.</summary>
public sealed record TsBindingName(string Name) : TsBindingTarget
{
    public override IEnumerable<string> BoundNames => [Name];
}

/// <summary>
/// An array pattern — <c>[a, , b]</c>. A null element is a hole: the
/// position is skipped and nothing is bound there.
/// </summary>
public sealed record TsArrayBindingPattern(IReadOnlyList<TsBindingTarget?> Elements)
    : TsBindingTarget
{
    public override IEnumerable<string> BoundNames =>
        Elements.OfType<TsBindingTarget>().SelectMany(element => element.BoundNames);
}

/// <summary>An object pattern — <c>{ x: a, y }</c>.</summary>
public sealed record TsObjectBindingPattern(IReadOnlyList<TsObjectBindingProperty> Properties)
    : TsBindingTarget
{
    public override IEnumerable<string> BoundNames =>
        Properties.SelectMany(property => property.Value.BoundNames);
}

/// <summary>One property of a <see cref="TsObjectBindingPattern"/>: the key read and its target.</summary>
public sealed record TsObjectBindingProperty(string Key, TsBindingTarget Value);
