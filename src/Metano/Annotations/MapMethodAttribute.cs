namespace Metano.Annotations;

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
public sealed class MapMethodAttribute(Type declaringType, string csharpMethod) : Attribute
{
    /// <summary>The C# type that declares the method (use an open generic for generics).</summary>
    public Type DeclaringType { get; } = declaringType;

    /// <summary>The C# method's simple name. All overloads are matched.</summary>
    public string CSharpMethod { get; } = csharpMethod;

    /// <summary>
    /// Simple JavaScript rename. Mutually exclusive with <see cref="JsTemplate"/>.
    /// Receiver and arguments are kept in their original positions.
    /// </summary>
    public string? JsMethod { get; init; }

    /// <summary>
    /// Full JavaScript expression template with <c>$this</c>, <c>$0</c>, <c>$1</c>, …
    /// placeholders. Mutually exclusive with <see cref="JsMethod"/>.
    ///
    /// Templates also support <c>$T0</c>, <c>$T1</c>, … placeholders which resolve to
    /// the call site's generic method type-argument names. So
    /// <c>JsTemplate = "$T0[$0 as keyof typeof $T0]"</c> applied to
    /// <c>Enum.Parse&lt;Status&gt;(text)</c> emits
    /// <c>Status[text as keyof typeof Status]</c>.
    /// </summary>
    public string? JsTemplate { get; init; }

    /// <summary>
    /// Optional literal-argument filter. When set, this declaration only matches if the
    /// call site's first argument is a string literal whose value equals this property.
    /// Used for literal-aware lowering like <c>Guid.ToString("N")</c> → strip hyphens vs
    /// the parameterless <c>Guid.ToString()</c> → identity.
    ///
    /// Multiple declarations for the same <c>(Type, Member)</c> pair are walked in source
    /// order; the first one whose filter matches the call site wins. Place specific
    /// filters before unfiltered fallback declarations.
    /// </summary>
    public string? WhenArg0StringEquals { get; init; }

    /// <summary>
    /// Optional source-receiver wrapping. When set, the call site's receiver is rewritten
    /// from <c>source.Method(args)</c> to <c>WrapReceiver(source).jsMethod(args)</c>
    /// (or to the equivalent template form). Used to inject a lazy-evaluation wrapper
    /// around extension method chains, e.g., LINQ on raw arrays:
    /// <c>arr.Where(p).Select(s)</c> → <c>Enumerable.from(arr).where(p).select(s)</c>.
    ///
    /// The transpiler skips re-wrapping when the receiver is already a chained call from
    /// the same wrapper — that is, either a direct call into the wrapper namespace
    /// (<c>Enumerable.from(...)</c>, <c>Enumerable.range(...)</c>, …) or a property access
    /// whose method name appears in any other declaration with the same
    /// <see cref="WrapReceiver"/> value. So a long fluent chain only wraps the very first
    /// call — subsequent ones recognize the wrapped shape and pass through.
    ///
    /// The wrapper string takes either the form <c>"Identifier"</c> (bare function call)
    /// or <c>"RootIdentifier.method"</c> (property access on a known root). Deeper paths
    /// are not supported yet.
    /// </summary>
    public string? WrapReceiver { get; init; }

    /// <summary>
    /// Optional runtime helper identifier that the lowered call site needs imported from
    /// <c>metano-runtime</c>. Used when a <see cref="JsTemplate"/> embeds a free
    /// identifier (e.g., <c>"dayNumber($this)"</c>) that the import collector cannot
    /// detect by walking the AST — the template body is opaque text from its perspective.
    ///
    /// When set, the transpiler treats the value as if it had been a referenced runtime
    /// identifier in the generated TS file, which causes the appropriate
    /// <c>import { … } from "metano-runtime";</c> line to be emitted.
    /// </summary>
    public string? RuntimeImports { get; init; }

    /// <summary>
    /// Dart-target counterpart of <see cref="JsMethod"/>. Same shape and semantics —
    /// when set, the Dart backend renames the call site to <c>$this.&lt;DartMethod&gt;(args)</c>
    /// (instance) or <c>&lt;DartMethod&gt;(args)</c> (static). Mutually exclusive with
    /// <see cref="DartTemplate"/>. A single attribute can declare both <c>Js*</c>
    /// and <c>Dart*</c> values; each target reads its own pair.
    /// </summary>
    public string? DartMethod { get; init; }

    /// <summary>
    /// Dart-target counterpart of <see cref="JsTemplate"/>. Supports the same
    /// <c>$this</c> / <c>$0</c> / <c>$1</c> / <c>$T0</c> placeholders. Mutually exclusive
    /// with <see cref="DartMethod"/>.
    /// </summary>
    public string? DartTemplate { get; init; }

    /// <summary>
    /// Dart-target counterpart of <see cref="RuntimeImports"/>. Comma-separated list of
    /// identifiers from <c>package:metano_runtime/metano_runtime.dart</c> that the
    /// lowered Dart call site needs to import.
    /// </summary>
    public string? DartRuntimeImports { get; init; }

    /// <summary>
    /// Optional argument-count filter. Contract: <c>-1</c> (default)
    /// disables the filter; any non-negative value requires the call
    /// site to carry exactly that many arguments. Used to disambiguate
    /// overloads when one shape lowers cleanly and another does not —
    /// e.g. <c>Console.WriteLine(value)</c> (single-arg, maps to
    /// Dart's <c>print(value)</c>) vs the format-string
    /// <c>Console.WriteLine(format, args...)</c> (no clean Dart
    /// equivalent). The sentinel is the cleanest option because the
    /// CLR rejects <c>int?</c> as an attribute argument type (CS0655).
    /// </summary>
    public int WhenArgCount { get; init; } = -1;
}
