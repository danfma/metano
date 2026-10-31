using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Parenthesization.Templates;

internal enum TsTemplateTokenKind
{
    /// <summary>A value placeholder: <c>$this</c> or <c>$N</c>.</summary>
    Placeholder,

    /// <summary>Identifier, keyword literal, number, string, regex, <c>$TN</c>.</summary>
    Operand,

    /// <summary>A keyword operator: <c>typeof</c>, <c>new</c>, <c>instanceof</c>, <c>as</c>, …</summary>
    Keyword,

    /// <summary>An operator or other punctuator (<c>+</c>, <c>?.</c>, <c>=&gt;</c>, …).</summary>
    Punctuator,

    /// <summary>An opening bracket: <c>(</c>, <c>[</c> or <c>{</c>.</summary>
    Open,

    /// <summary>A closing bracket: <c>)</c>, <c>]</c> or <c>}</c>.</summary>
    Close,

    /// <summary>A <c>,</c> separator (or sequence operator at the top level).</summary>
    Comma,
}

/// <summary>
/// One lexical token of a <see cref="TsTemplate"/> text. <see cref="Start"/>
/// is the character offset in the template, which is how the printer
/// matches a placeholder occurrence to its analyzed context.
/// </summary>
internal readonly record struct TsTemplateToken(
    TsTemplateTokenKind Kind,
    string Text,
    int Start,
    TsTemplatePlaceholder? Placeholder = null
)
{
    /// <summary>
    /// Whether the token can end an operand, which makes a following
    /// <c>-</c>/<c>+</c> binary and a following <c>/</c> a division.
    /// </summary>
    public bool EndsOperand =>
        Kind
            is TsTemplateTokenKind.Operand
                or TsTemplateTokenKind.Placeholder
                or TsTemplateTokenKind.Close;

    public int DepthDelta =>
        Kind switch
        {
            TsTemplateTokenKind.Open => 1,
            TsTemplateTokenKind.Close => -1,
            _ => 0,
        };

    public bool Is(TsTemplateTokenKind kind, string text) => Kind == kind && Text == text;
}
