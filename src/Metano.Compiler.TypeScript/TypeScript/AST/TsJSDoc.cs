namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A JSDoc comment printed ahead of the declaration it annotates: the
/// printer renders each entry as its own <c>/** text *&#47;</c> comment on its
/// own line. The tag text is emitted verbatim — tags keep the leading
/// <c>@</c>.
/// </summary>
public sealed record TsJSDoc(IReadOnlyList<string> Lines);
