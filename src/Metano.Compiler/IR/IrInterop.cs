using Metano.Annotations;

namespace Metano.Compiler.IR;

/// <summary>
/// Where a piece of source sits, without a Roslyn dependency, so a target can
/// report a diagnostic it decides itself (spec 011 FR-031's <c>MS0056</c>):
/// the file, the character span and its zero-based line / character bounds.
/// </summary>
public sealed record IrSourceSpan(
    string FilePath,
    int Start,
    int Length,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter
);

/// <summary>A part of inline target code: verbatim text or a transpiled hole.</summary>
public abstract record IrInlineCodePart;

/// <summary>Verbatim target text, its C# escapes already resolved (<c>{{</c> → <c>{</c>).</summary>
public sealed record IrInlineCodeText(string Text) : IrInlineCodePart;

/// <summary>
/// An interpolation hole: a C# expression transpiled by the normal pipeline and
/// spliced as an operand, with its source span for target diagnostics.
/// </summary>
public sealed record IrInlineCodeHole(IrExpression Expression, IrSourceSpan? Span)
    : IrInlineCodePart;

/// <summary>
/// Inline target code in expression position (<c>Js.Code("…")</c>, spec 011
/// FR-031).
/// </summary>
/// <param name="Parts">The text and holes, in source order.</param>
/// <param name="ResultType">The type argument of <c>Js.Code&lt;T&gt;</c>;
/// <c>null</c> for the untyped <c>Js.Code</c>, whose C# result is
/// <c>dynamic</c>.</param>
/// <param name="Language">The target language the API belongs to
/// (<see cref="TargetLanguage.TypeScript"/> for <c>Metano.Interop.Js</c>): a fact
/// of the API, not of the running emission.</param>
/// <param name="Span">The call's source span.</param>
public sealed record IrInlineCodeExpression(
    IReadOnlyList<IrInlineCodePart> Parts,
    IrTypeRef? ResultType,
    TargetLanguage Language,
    IrSourceSpan? Span
) : IrExpression;

/// <summary>
/// Inline target code in statement position: a <c>Js.Code</c> call that is an
/// expression statement, an expression-bodied <c>void</c> member included. The
/// target decides how the text ends.
/// </summary>
/// <remarks>Its <see cref="IrStatement.Span"/> is the call's, where a target reports
/// an invalid text.</remarks>
public sealed record IrInlineCodeStatement(
    IReadOnlyList<IrInlineCodePart> Parts,
    TargetLanguage Language
) : IrStatement;

/// <summary>
/// <c>value.UnsafeCast&lt;T&gt;()</c>: a compile-time-only reinterpretation, with
/// no runtime check, descriptor or helper (spec 011 FR-035).
/// </summary>
public sealed record IrUnsafeCastExpression(IrExpression Value, IrTypeRef Target) : IrExpression;

/// <summary>
/// <c>value.AsDynamic()</c>: the value seen as <c>dynamic</c>, with no runtime
/// effect (spec 011 FR-036).
/// </summary>
public sealed record IrAsDynamicExpression(IrExpression Value) : IrExpression;

/// <summary>The holes of inline target code.</summary>
public static class IrInlineCodeParts
{
    /// <summary>The hole expressions of <paramref name="parts"/>, in source order.</summary>
    public static IEnumerable<IrExpression> HolesOf(IReadOnlyList<IrInlineCodePart> parts) =>
        parts.OfType<IrInlineCodeHole>().Select(hole => hole.Expression);

    /// <summary><paramref name="parts"/> with every hole expression rewritten.</summary>
    public static IReadOnlyList<IrInlineCodePart> MapHoles(
        IReadOnlyList<IrInlineCodePart> parts,
        Func<IrExpression, IrExpression> rewrite
    ) =>
        [
            .. parts.Select(part =>
                part is IrInlineCodeHole hole
                    ? hole with
                    {
                        Expression = rewrite(hole.Expression),
                    }
                    : part
            ),
        ];
}
