namespace Metano.Compiler;

/// <summary>
/// Declares a static asset that the transpiler should copy from the source
/// project into <see cref="TranspileOptions.OutputDir"/> alongside generated
/// files. Surfaced via the <c>--asset</c> CLI flag (one entry per
/// occurrence) and ultimately driven by <c>&lt;MetanoAsset Include="..." /&gt;</c>
/// items in the consumer's csproj.
/// </summary>
/// <param name="Source">
/// Absolute path to the source file on disk. The MSBuild target resolves
/// this from <c>%(MetanoAsset.FullPath)</c> before invoking the CLI.
/// </param>
/// <param name="RelativeDestination">
/// Output-relative destination path. When <see langword="null"/>, the host
/// mirrors the source's path relative to the project directory (so
/// <c>schema.sql</c> at the project root lands at
/// <c>OutputDir/schema.sql</c>, and <c>assets/seed.json</c> lands at
/// <c>OutputDir/assets/seed.json</c>). When supplied, this value
/// replaces the mirrored path entirely.
/// </param>
public sealed record AssetSpec(string Source, string? RelativeDestination = null);
