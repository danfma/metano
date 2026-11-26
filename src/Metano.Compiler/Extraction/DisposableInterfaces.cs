using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Recognizes <c>System.IDisposable</c> and the targets that declare it: TypeScript maps it
/// to the runtime interface <c>IDisposable { dispose(): void }</c> (FR-111); Dart declares
/// none, so it keeps the rules of every other BCL interface there.
/// </summary>
internal static class DisposableInterfaces
{
    /// <summary>Whether <paramref name="candidate"/> is <c>System.IDisposable</c>.</summary>
    public static bool Is(INamedTypeSymbol candidate) =>
        candidate.SpecialType is SpecialType.System_IDisposable;

    /// <summary>Whether <paramref name="target"/> declares <c>System.IDisposable</c>.</summary>
    public static bool IsDeclaredBy(TargetLanguage? target) => target is not TargetLanguage.Dart;
}
