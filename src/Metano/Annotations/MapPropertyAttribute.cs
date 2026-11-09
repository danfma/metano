namespace Metano.Annotations;

/// <summary>
/// Declarative mapping from a C# property to its TypeScript equivalent. Applied at the
/// assembly level, multiple times per assembly. Read by the transpiler at compile time
/// to drive the BCL → JavaScript lowering for accesses of the named property.
///
/// Two forms are supported, mutually exclusive:
///
/// <list type="bullet">
///   <item>
///     <see cref="JsProperty"/> — simple rename. The access becomes
///     <c>&lt;receiver&gt;.&lt;JsProperty&gt;</c> for instance properties or
///     <c>&lt;JsProperty&gt;</c> for static ones.
///   </item>
///   <item>
///     <see cref="JsTemplate"/> — full template with <c>$this</c> as the instance
///     receiver placeholder. Useful for properties whose JS form is more than a name
///     (e.g., a method call instead of a property access).
///   </item>
/// </list>
///
/// Generic types: pass an open generic via <c>typeof(List&lt;&gt;)</c>; the transpiler
/// compares against the symbol's <c>OriginalDefinition</c> so the mapping applies to all
/// instantiations.
/// </summary>
/// <example>
/// <code>
/// // Simple rename: list.Count → list.length
/// [assembly: MapProperty(typeof(List&lt;&gt;), "Count", JsProperty = "length")]
///
/// // Dictionary&lt;K,V&gt;.Count → dict.size
/// [assembly: MapProperty(typeof(Dictionary&lt;,&gt;), "Count", JsProperty = "size")]
///
/// // Template: DateTime.Now → Temporal.Now.plainDateTimeISO()
/// [assembly: MapProperty(typeof(DateTime), "Now",
///     JsTemplate = "Temporal.Now.plainDateTimeISO()")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MapPropertyAttribute(Type declaringType, string csharpProperty) : Attribute
{
    /// <summary>The C# type that declares the property (use an open generic for generics).</summary>
    public Type DeclaringType { get; } = declaringType;

    /// <summary>The C# property's name.</summary>
    public string CSharpProperty { get; } = csharpProperty;

    /// <summary>
    /// Simple JavaScript rename. Mutually exclusive with <see cref="JsTemplate"/>.
    /// </summary>
    public string? JsProperty { get; init; }

    /// <summary>
    /// Full JavaScript expression template with a <c>$this</c> placeholder for the
    /// instance receiver. Mutually exclusive with <see cref="JsProperty"/>.
    /// </summary>
    public string? JsTemplate { get; init; }

    /// <summary>
    /// Optional runtime helper identifier that the lowered access needs imported from
    /// <c>metano-runtime</c>. Used when a <see cref="JsTemplate"/> embeds a free
    /// identifier (e.g., <c>"dayNumber($this)"</c>) that the import collector cannot
    /// detect by walking the AST. See <c>MapMethodAttribute.RuntimeImports</c>.
    /// </summary>
    public string? RuntimeImports { get; init; }

    /// <summary>
    /// Dart-target counterpart of <see cref="JsProperty"/>. Mutually exclusive with
    /// <see cref="DartTemplate"/>. A single attribute can declare both <c>Js*</c> and
    /// <c>Dart*</c> values; each target reads its own pair.
    /// </summary>
    public string? DartProperty { get; init; }

    /// <summary>
    /// Dart-target counterpart of <see cref="JsTemplate"/>. Supports <c>$this</c>
    /// for the instance receiver. Mutually exclusive with <see cref="DartProperty"/>.
    /// </summary>
    public string? DartTemplate { get; init; }

    /// <summary>
    /// Dart-target counterpart of <see cref="RuntimeImports"/>. Comma-separated list
    /// of identifiers from <c>package:metano_runtime/metano_runtime.dart</c>.
    /// </summary>
    public string? DartRuntimeImports { get; init; }
}
