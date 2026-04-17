namespace Metano.Annotations;

/// <summary>
/// Overrides the name used in generated target code. The attribute can be
/// applied multiple times on the same symbol — one per target plus an
/// optional global form — so the same C# symbol can carry different renames
/// without duplicating attribute lists.
/// <para>
/// Resolution rules when a backend looks up a symbol's override:
/// <list type="number">
///   <item>If a <c>[Name(Target, "…")]</c> exists with <c>Target</c> equal to
///   the emitting backend, that name wins.</item>
///   <item>Otherwise, the untargeted <c>[Name("…")]</c> (if any) is used.</item>
///   <item>Otherwise, each target applies its own naming rules (PascalCase
///   preservation for types, camelCase for members, etc.).</item>
/// </list>
/// </para>
/// <para>
/// Example — a C# interface <c>ICounterView</c> that should drop the <c>I</c>
/// prefix on both targets:
/// <code>
/// [Name(TargetLanguage.TypeScript, "CounterView")]
/// [Name(TargetLanguage.Dart, "CounterView")]
/// public interface ICounterView { … }
/// </code>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public sealed class NameAttribute : Attribute
{
    /// <summary>Untargeted override — applies to every backend that lacks a
    /// target-specific override.</summary>
    public NameAttribute(string name)
    {
        Name = name;
        Target = null;
    }

    /// <summary>Per-target override — applies only when
    /// <paramref name="target"/> matches the emitting backend.</summary>
    public NameAttribute(TargetLanguage target, string name)
    {
        Name = name;
        Target = target;
    }

    public string Name { get; }

    /// <summary>The target this override applies to, or <c>null</c> for the
    /// untargeted form.</summary>
    public TargetLanguage? Target { get; }
}
