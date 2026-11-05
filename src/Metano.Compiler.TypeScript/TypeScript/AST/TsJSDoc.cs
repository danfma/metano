namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A JSDoc comment printed ahead of the member it annotates: the printer
/// renders one line as <c>/** text *&#47;</c> and several lines as a stacked
/// <c>/** ... *&#47;</c> block, one line per entry. The tag text is emitted
/// verbatim — tags keep the leading <c>@</c>.
/// </summary>
public sealed record TsJSDoc(IReadOnlyList<string> Lines);
