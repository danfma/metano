namespace MetaSharp.Annotations;

/// <summary>
/// Declarative mapping from a C# method to its TypeScript equivalent. Applied at the
/// assembly level, multiple times per assembly. Read by the transpiler at compile time
/// to drive the BCL → JavaScript lowering for invocations of the named method.
///
/// Two forms are supported, mutually exclusive:
///
/// <list type="bullet">
///   <item>
///     <see cref="JsMethod"/> — simple rename. For instance methods, the call site
///     becomes <c>&lt;receiver&gt;.&lt;JsMethod&gt;(args…)</c>. For static methods, the
///     call site becomes <c>&lt;JsMethod&gt;(args…)</c> (the C# qualifier is dropped).
///   </item>
///   <item>
///     <see cref="JsTemplate"/> — full template with placeholders. <c>$this</c> stands
///     for the instance receiver (instance methods only); <c>$0</c>, <c>$1</c>, … stand
///     for the C# method's explicit parameters in order. Same convention as
///     <see cref="EmitAttribute"/>.
///   </item>
/// </list>
///
/// When the C# method has overloads, the attribute matches all of them by name. Use
/// <see cref="JsTemplate"/> if a specific overload needs different lowering — overload
/// disambiguation by parameter type list will be added if/when needed.
///
/// Generic types: pass an open generic via <c>typeof(List&lt;&gt;)</c>; the transpiler
/// compares against the symbol's <c>OriginalDefinition</c> so the mapping applies to all
/// instantiations.
/// </summary>
/// <example>
/// <code>
/// // Simple rename: list.Add(x) → list.push(x)
/// [assembly: MapMethod(typeof(List&lt;&gt;), "Add", JsMethod = "push")]
///
/// // Static rename: Enumerable.Empty&lt;T&gt;() → empty()
/// [assembly: MapMethod(typeof(Enumerable), "Empty", JsMethod = "empty")]
///
/// // Template: string.IsNullOrEmpty(s) → (s == null || s === "")
/// [assembly: MapMethod(typeof(string), "IsNullOrEmpty",
///     JsTemplate = "($0 == null || $0 === \"\")")]
///
/// // Instance method template: list.RemoveAt(i) → list.splice(i, 1)
/// [assembly: MapMethod(typeof(List&lt;&gt;), "RemoveAt",
///     JsTemplate = "$this.splice($0, 1)")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MapMethodAttribute : Attribute
{
    /// <summary>The C# type that declares the method (use an open generic for generics).</summary>
    public Type DeclaringType { get; }

    /// <summary>The C# method's simple name. All overloads are matched.</summary>
    public string CSharpMethod { get; }

    /// <summary>
    /// Simple JavaScript rename. Mutually exclusive with <see cref="JsTemplate"/>.
    /// Receiver and arguments are kept in their original positions.
    /// </summary>
    public string? JsMethod { get; init; }

    /// <summary>
    /// Full JavaScript expression template with <c>$this</c>, <c>$0</c>, <c>$1</c>, …
    /// placeholders. Mutually exclusive with <see cref="JsMethod"/>.
    /// </summary>
    public string? JsTemplate { get; init; }

    public MapMethodAttribute(Type declaringType, string csharpMethod)
    {
        DeclaringType = declaringType;
        CSharpMethod = csharpMethod;
    }
}
