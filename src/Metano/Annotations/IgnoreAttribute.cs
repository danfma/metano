namespace Metano.Annotations;

/// <summary>
/// Excludes a member from transpilation.
/// <para>
/// Follows the same per-target resolution pattern as <see cref="NameAttribute"/>:
/// multiple <c>[Ignore]</c> occurrences can coexist on the same symbol so a
/// single C# member can be skipped on one backend while still participating
/// in another.
/// <list type="number">
///   <item><c>[Ignore(TargetLanguage.X)]</c> applies only when target
///   <c>X</c> is emitting.</item>
///   <item>The parameterless <c>[Ignore]</c> applies to every target that
///   lacks a per-target <c>[Ignore]</c>.</item>
/// </list>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
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
