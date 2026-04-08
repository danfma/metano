namespace MetaSharp.Annotations;

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
public sealed class MapPropertyAttribute : Attribute
{
    /// <summary>The C# type that declares the property (use an open generic for generics).</summary>
    public Type DeclaringType { get; }

    /// <summary>The C# property's name.</summary>
    public string CSharpProperty { get; }

    /// <summary>
    /// Simple JavaScript rename. Mutually exclusive with <see cref="JsTemplate"/>.
    /// </summary>
    public string? JsProperty { get; init; }

    /// <summary>
    /// Full JavaScript expression template with a <c>$this</c> placeholder for the
    /// instance receiver. Mutually exclusive with <see cref="JsProperty"/>.
    /// </summary>
    public string? JsTemplate { get; init; }

    public MapPropertyAttribute(Type declaringType, string csharpProperty)
    {
        DeclaringType = declaringType;
        CSharpProperty = csharpProperty;
    }
}
