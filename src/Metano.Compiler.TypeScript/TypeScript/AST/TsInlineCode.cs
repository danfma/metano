namespace Metano.Compiler.TypeScript.AST;

/// <summary>A part of inline TypeScript: verbatim text or a spliced hole.</summary>
public abstract record TsInlineCodePart;

/// <summary>Verbatim TypeScript text, printed as is.</summary>
public sealed record TsInlineCodeText(string Text) : TsInlineCodePart;

/// <summary>
/// A line break the bridge adds after the text (the <c>;</c> that ends a statement
/// whose text ends in a <c>//</c> comment), printed at the statement's indentation.
/// </summary>
public sealed record TsInlineCodeLineBreak : TsInlineCodePart;

/// <summary>
/// A transpiled hole, printed in the inline-code operand slot: parenthesised when
/// it binds looser than a member access, is an unsigned numeric literal, or starts
/// with an object literal or a function expression.
/// </summary>
public sealed record TsInlineCodeHole(TsExpression Expression) : TsInlineCodePart;

/// <summary>
/// Inline TypeScript in expression position (<c>Js.Code</c>, spec 011 FR-031),
/// printed in parentheses: <c>(text)</c>. The bridge adds the <c>as T</c> around it.
/// </summary>
public sealed record TsInlineCode(IReadOnlyList<TsInlineCodePart> Parts) : TsExpression;

/// <summary>
/// Inline TypeScript in statement position, printed as is at the statement's
/// indentation. The bridge has already ended it (<c>;</c>), so the printer adds
/// nothing.
/// </summary>
public sealed record TsInlineCodeStatement(IReadOnlyList<TsInlineCodePart> Parts) : TsStatement
{
    /// <summary>The hole expressions, for AST walkers such as the import collector.</summary>
    public IEnumerable<TsExpression> Holes => TsInlineCodeHoles.Of(Parts);
}

/// <summary>The holes of inline TypeScript.</summary>
public static class TsInlineCodeHoles
{
    /// <summary>The hole expressions of <paramref name="parts"/>, in order.</summary>
    public static IEnumerable<TsExpression> Of(IReadOnlyList<TsInlineCodePart> parts) =>
        parts.OfType<TsInlineCodeHole>().Select(hole => hole.Expression);
}
