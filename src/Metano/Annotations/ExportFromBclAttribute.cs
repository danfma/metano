namespace Metano.Annotations;

/// <summary>
/// Maps a BCL type to a JavaScript package type.
/// Applied at assembly level to configure type mappings for transpilation.
/// </summary>
/// <example>
/// [assembly: ExportFromBcl(typeof(decimal), FromPackage = "decimal.js", ExportedName = "Decimal")]
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ExportFromBclAttribute(Type type) : Attribute
{
    public Type Type { get; } = type;
    public string FromPackage { get; init; } = "";
    public string ExportedName { get; init; } = "";

    /// <summary>
    /// Optional npm version specifier (e.g., <c>^10.6.0</c>). When set, the compiler
    /// auto-adds <c>{ FromPackage: Version }</c> to the consumer's
    /// <c>package.json#dependencies</c> the first time a type referenced by this
    /// mapping is actually used. Without it, the user is responsible for adding the
    /// dependency manually.
    /// </summary>
    public string Version { get; init; } = "";
}
