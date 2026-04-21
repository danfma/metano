namespace Metano.Annotations;

/// <summary>
/// Identifies the target language/ecosystem for an <see cref="EmitPackageAttribute"/>
/// declaration. The Metano compiler has one binary per target (e.g.,
/// <c>metano-typescript</c>) and only consumes <c>[EmitPackage]</c> declarations
/// whose <see cref="Target"/> matches the target it's emitting for.
///
/// <para>
/// New targets are added here as the compiler grows. Each language has its own naming
/// conventions for the package name on the C# side (e.g., <c>"@scope/name"</c> for
/// <see cref="JavaScript"/>, <c>snake_case</c> for a future Dart target,
/// <c>group:artifact</c> for a future Maven target).
/// </para>
/// </summary>
public enum EmitTarget
{
    /// <summary>The TypeScript / JavaScript target (npm package name).</summary>
    JavaScript = 0,

    /// <summary>The Dart / Flutter target (pub.dev package name).</summary>
    Dart = 1,
}
