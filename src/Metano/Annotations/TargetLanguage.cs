namespace Metano.Annotations;

/// <summary>
/// Identifies a transpilation target. Used as the discriminator for
/// target-specific attributes (see <see cref="NameAttribute"/>) and anywhere
/// the compiler needs to key on "which backend is emitting".
/// </summary>
public enum TargetLanguage
{
    TypeScript,
    Dart,
}

/// <summary>
/// Maps between the symbolic <see cref="TargetLanguage"/> used by the
/// compiler pipeline and the <see cref="EmitTarget"/> discriminator
/// baked into <see cref="EmitPackageAttribute"/>. The two enums stay
/// separate because <c>[EmitPackage]</c> ships on the consumer's C#
/// surface and keeps its own attribute-layer discriminator.
/// </summary>
public static class TargetLanguageExtensions
{
    public static EmitTarget ToEmitTarget(this TargetLanguage target) =>
        target switch
        {
            TargetLanguage.TypeScript => EmitTarget.JavaScript,
            TargetLanguage.Dart => EmitTarget.Dart,
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                $"No EmitTarget mapping for TargetLanguage.{target}."
            ),
        };
}
