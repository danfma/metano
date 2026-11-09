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
/// <para>
/// On one overload of a dispatched group — a constructor or method overload
/// (operators, module functions and extensions included) — the name pins the
/// overload's generated <em>suffix</em> only, capitalized; the dispatcher keeps
/// the C# name: <c>[Name("cents")]</c> on <c>Money(int c)</c> emits
/// <c>Money.fromCents</c> and the selector <c>byMoneyCents</c>, on
/// <c>Add(int c)</c> the fast path <c>addCents</c>. The value must start with an
/// ASCII letter or <c>_</c> and contain only ASCII letters, digits and
/// <c>_</c>. An override or interface implementation takes its root's name, so
/// its own <c>[Name]</c> is ignored.
/// </para>
/// </summary>
[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Interface
        | AttributeTargets.Delegate
        | AttributeTargets.Method
        | AttributeTargets.Constructor
        | AttributeTargets.Property
        | AttributeTargets.Field
        | AttributeTargets.Parameter
        | AttributeTargets.Event,
    AllowMultiple = true
)]
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
