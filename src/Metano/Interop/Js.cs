using Metano.Annotations;

namespace Metano.Interop;

/// <summary>
/// Inline TypeScript for one-off glue that needs no declaration (spec 011 FR-031).
/// The <c>code</c> argument is a compile-time constant string, or an interpolated
/// string whose holes are C# expressions: the text is emitted verbatim and each hole
/// is transpiled and spliced as an operand. In expression position the text is
/// wrapped in parentheses (<c>(text) as any</c>, or <c>(text) as T</c> for
/// <see cref="Code{T}(string)"/>); as an expression statement it is emitted as
/// statement text. The text is opaque: it pulls no import and no diagnostic sees
/// inside it, so anything called twice or exposed in a signature belongs in an
/// <c>[Import]</c> / <c>[Emit]</c> facade instead. TypeScript only: any other target
/// reports <c>MS0051</c>. On .NET both methods throw
/// <see cref="PlatformNotSupportedException"/>.
/// </summary>
[TargetSpecific(TargetLanguage.TypeScript)]
public static class Js
{
    /// <summary>Inline TypeScript whose value is untyped (<c>dynamic</c>).</summary>
    /// <param name="code">The TypeScript text; a constant or an interpolated string.</param>
    public static dynamic Code(string code) => throw NotTranspiled();

    /// <summary>Inline TypeScript whose value the caller states as <typeparamref name="T"/>.</summary>
    /// <param name="code">The TypeScript text; a constant or an interpolated string.</param>
    public static T Code<T>(string code) => throw NotTranspiled();

    private static PlatformNotSupportedException NotTranspiled() =>
        new("Js.Code runs only in transpiled TypeScript.");
}
