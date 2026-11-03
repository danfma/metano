using System.Reflection;

namespace Metano.Compiler;

/// <summary>
/// The release identity of the running compiler. Every Metano compiler assembly (the core
/// and each language target) is built from one repository tag, so they share one version:
/// the release pipeline packs them with <c>-p:MinVerVersionOverride=X</c>, and MinVer is what
/// sets the assembly's informational version (<c>-p:Version</c> alone does not reach it).
/// </summary>
public static class CompilerRelease
{
    /// <summary>
    /// The core assembly's informational version (e.g. <c>2.10.0</c>, or
    /// <c>2.10.1-preview.0.3+&lt;sha&gt;</c> for a local build); empty when the attribute is
    /// missing. Constant for the lifetime of the process.
    /// </summary>
    public static string Version { get; } = ReadInformationalVersion();

    /// <summary>
    /// <see cref="FingerprintSegment"/> of the running release: the segment every
    /// incremental cache this process writes leads with.
    /// </summary>
    public static string CurrentFingerprintSegment { get; } = FingerprintSegment(Version);

    /// <summary>
    /// The leading segment of a cache configuration fingerprint (ADR-0021) that pins
    /// <paramref name="release"/>, so a cache another release wrote is a miss (#433).
    /// </summary>
    public static string FingerprintSegment(string release) => $"compiler={release};";

    private static string ReadInformationalVersion() =>
        typeof(CompilerRelease)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? string.Empty;
}
