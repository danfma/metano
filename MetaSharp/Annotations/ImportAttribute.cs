namespace MetaSharp.Annotations;

/// <summary>
/// Declares that a type or member is imported from an external JavaScript module.
/// The type body is not transpiled — only the import statement is generated.
///
/// <para>By default, generates a named import: <c>import { Name } from "from";</c>.
/// Set <see cref="AsDefault"/> to <c>true</c> to generate a default import instead:
/// <c>import Name from "from";</c>. This is the right form for libraries whose primary
/// export is the type itself (e.g., a UMD-style class).</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property)]
public sealed class ImportAttribute(string name, string from) : Attribute
{
    public string Name { get; } = name;
    public string From { get; } = from;

    /// <summary>
    /// When true, the generated import uses default-import syntax
    /// (<c>import Name from "from"</c>) instead of named-import syntax.
    /// </summary>
    public bool AsDefault { get; init; }
}
