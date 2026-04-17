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
