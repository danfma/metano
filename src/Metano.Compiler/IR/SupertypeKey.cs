namespace Metano.Compiler.IR;

/// <summary>
/// The identity <see cref="IrNamedTypeSemantics.Supertypes"/> lists a type under: its
/// namespace and its name, without type arguments, so <c>Box&lt;int&gt;</c> and
/// <c>Box&lt;string&gt;</c> share one key.
/// </summary>
public readonly record struct SupertypeKey
{
    private SupertypeKey(string? @namespace, string name)
    {
        Namespace = @namespace;
        Name = name;
    }

    /// <summary>The containing namespace; <c>null</c> for the global namespace.</summary>
    public string? Namespace { get; }

    /// <summary>The type's (containing-type-qualified) name.</summary>
    public string Name { get; }

    public static SupertypeKey From(string? @namespace, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new SupertypeKey(@namespace, name);
    }
}
