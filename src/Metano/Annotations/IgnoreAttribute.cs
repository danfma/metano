namespace Metano.Annotations;

/// <summary>
/// Marks a type or member as <em>.NET-only</em>: it exists in the C# type
/// system for internal logic, source-generator scaffolding, or build-time
/// tooling, but must never cross into transpiled code. The transpiler emits
/// <c>MS0013 IgnoreReferencedByTranspiledCode</c> at every reference from a
/// transpilable type's surface area (signature or body) so the boundary
/// stays explicit — predictable compile-time errors instead of runtime
/// surprises in the generated output.
///
/// <code>
/// [Ignore]
/// public sealed class BuildToolingMarker { }
///
/// public static class InternalBookkeeping
/// {
///     public static List&lt;BuildToolingMarker&gt; Cache { get; } = new();
/// }
///
/// [Transpile]
/// public class Bad
/// {
///     public BuildToolingMarker Field; // raises MS0013
/// }
/// </code>
///
/// Members can also be ignored individually — useful for the rare case
/// where a type is mostly transpilable but a single method/property/field
/// is .NET-only.
///
/// For ambient TypeScript shapes that <em>do</em> need to be referenced
/// from transpiled code (DOM types, structural shapes, virtual-DOM nodes),
/// use <see cref="TypeScript.ExternalAttribute"/> instead: it expresses
/// "no emission" without painting transpilable code as broken.
/// <para>
/// Resolution rules when a backend looks up whether a symbol is ignored:
/// <list type="number">
///   <item>If an <c>[Ignore(Target)]</c> exists with <c>Target</c> equal to
///   the emitting backend, the symbol is ignored on that target.</item>
///   <item>Otherwise, the untargeted <c>[Ignore]</c> (if any) ignores the
///   symbol on every target.</item>
///   <item>Otherwise, the symbol participates in emission normally.</item>
/// </list>
/// Mirrors the pattern carried by <see cref="NameAttribute"/>.
/// </para>
/// <para>
/// Parameter targets are intentionally excluded — dropping a parameter from
/// a method signature on the emit side while keeping it in the C# call
/// shifts every subsequent positional argument and corrupts the call ABI at
/// runtime. Use a wrapper method instead.
/// </para>
/// </summary>
[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Interface
        | AttributeTargets.Method
        | AttributeTargets.Property
        | AttributeTargets.Field
        | AttributeTargets.Event,
    AllowMultiple = true
)]
public sealed class IgnoreAttribute : Attribute
{
    public IgnoreAttribute()
    {
        Target = null;
    }

    public IgnoreAttribute(TargetLanguage target)
    {
        Target = target;
    }

    /// <summary>The target this ignore applies to, or <c>null</c> for the
    /// untargeted (global) form.</summary>
    public TargetLanguage? Target { get; }
}
