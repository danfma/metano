using System.Collections.Concurrent;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Parenthesization.Templates;

/// <summary>
/// What the parenthesization rules need to know about a
/// <see cref="TsTemplate"/> text, which is otherwise opaque to the AST:
/// <list type="bullet">
///   <item><see cref="TopLevelOperator"/> — how the whole template binds
///   when it is itself an operand (<c>$0 + $1</c> is additive,
///   <c>$this.length = 0</c> is an assignment, <c>new Decimal($0)</c> is
///   member-level);</item>
///   <item><see cref="TransparentSubstitution"/> — a template that is a
///   single placeholder (<c>$this</c>) binds like its substitution;</item>
///   <item><see cref="SlotsAt"/> — for each placeholder occurrence, the
///   slots imposed by its nearest neighbor token on each side, so the
///   printer wraps a substituted argument only where its position needs it
///   (<c>($this &amp; $0) === $0</c> wraps an <c>a | b</c> argument,
///   <c>console.log($0)</c> never does).</item>
/// </list>
/// Shapes are memoized per template string for the life of the process.
/// The key space is the BCL template set plus the <c>[Emit]</c> texts of the
/// compiled sources — bounded by the source, and each entry is a few small
/// objects — so the cache is not evicted.
/// </summary>
internal sealed class TsTemplateShape
{
    private static readonly ConcurrentDictionary<string, TsTemplateShape> Cache = new();

    /// <summary>
    /// Text the lexer cannot analyze binds as <see cref="TsPrecedence.Lowest"/>,
    /// and every placeholder gets the access-target slot, so substitutions
    /// below member level are wrapped — over-wrapping rather than miscompiling.
    /// </summary>
    private static readonly TsTemplateShape Unknown = new(
        new TsTemplateTopLevelOperator(TsPrecedence.Lowest, Token: null),
        lonePlaceholder: null,
        leadingPlaceholder: null,
        isObjectLiteral: false,
        new Dictionary<int, IReadOnlyList<TsOperandSlot>>(),
        defaultSlots: [TsOperandSlot.AccessTarget]
    );

    private readonly TsTemplatePlaceholder? _lonePlaceholder;
    private readonly TsTemplatePlaceholder? _leadingPlaceholder;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<TsOperandSlot>> _slotsByOffset;
    private readonly IReadOnlyList<TsOperandSlot> _defaultSlots;

    private TsTemplateShape(
        TsTemplateTopLevelOperator topLevelOperator,
        TsTemplatePlaceholder? lonePlaceholder,
        TsTemplatePlaceholder? leadingPlaceholder,
        bool isObjectLiteral,
        IReadOnlyDictionary<int, IReadOnlyList<TsOperandSlot>> slotsByOffset,
        IReadOnlyList<TsOperandSlot> defaultSlots
    )
    {
        TopLevelOperator = topLevelOperator;
        _lonePlaceholder = lonePlaceholder;
        _leadingPlaceholder = leadingPlaceholder;
        IsObjectLiteral = isObjectLiteral;
        _slotsByOffset = slotsByOffset;
        _defaultSlots = defaultSlots;
    }

    public TsTemplateTopLevelOperator TopLevelOperator { get; }

    /// <summary>
    /// The whole text is one <c>{ … }</c> group, taken to be an object
    /// literal: templates are expressions, so block-shaped <c>[Emit]</c> text
    /// is not supported.
    /// </summary>
    public bool IsObjectLiteral { get; }

    /// <summary>
    /// The slots of the placeholder that starts at <paramref name="offset"/>
    /// in the template text; empty when the placeholder sits between
    /// delimiters (<c>(</c>/<c>,</c> and <c>)</c>/<c>,</c>/<c>]</c>) or
    /// inside a string the lexer does not look into.
    /// </summary>
    public IReadOnlyList<TsOperandSlot> SlotsAt(int offset) =>
        _slotsByOffset.TryGetValue(offset, out var slots) ? slots : _defaultSlots;

    public static TsTemplateShape Of(TsTemplate template) =>
        Cache.GetOrAdd(template.Template, Analyze);

    /// <summary>
    /// The substituted node when <paramref name="template"/> is nothing but
    /// one resolvable value placeholder; the template is then transparent.
    /// </summary>
    public static TsExpression? TransparentSubstitution(TsTemplate template) =>
        Of(template)._lonePlaceholder?.Resolve(template);

    /// <summary>
    /// The substituted node of the placeholder the template text starts
    /// with (<c>$0</c> in <c>$0.foo()</c>), whose own text is printed first.
    /// </summary>
    public static TsExpression? LeadingSubstitution(TsTemplate template) =>
        Of(template)._leadingPlaceholder?.Resolve(template);

    private static TsTemplateShape Analyze(string template)
    {
        var tokens = TsTemplateLexer.Tokenize(template);

        if (tokens is null || !AreBalanced(tokens))
            return Unknown;

        return new TsTemplateShape(
            TsTemplatePrecedence.Classify(tokens),
            LonePlaceholderOf(tokens),
            LeadingPlaceholderOf(tokens),
            IsObjectLiteralOf(tokens),
            SlotsByOffset(tokens),
            defaultSlots: []
        );
    }

    private static bool AreBalanced(IReadOnlyList<TsTemplateToken> tokens)
    {
        var depth = 0;

        foreach (var token in tokens)
        {
            depth += token.DepthDelta;

            if (depth < 0)
                return false;
        }

        return depth == 0;
    }

    private static TsTemplatePlaceholder? LonePlaceholderOf(
        IReadOnlyList<TsTemplateToken> tokens
    ) => tokens is [{ Kind: TsTemplateTokenKind.Placeholder } only] ? only.Placeholder : null;

    private static TsTemplatePlaceholder? LeadingPlaceholderOf(
        IReadOnlyList<TsTemplateToken> tokens
    ) => tokens is [{ Kind: TsTemplateTokenKind.Placeholder } first, ..] ? first.Placeholder : null;

    private static bool IsObjectLiteralOf(IReadOnlyList<TsTemplateToken> tokens) =>
        tokens is [{ Kind: TsTemplateTokenKind.Open, Text: "{" }, ..]
        && TsTemplatePrecedence.IsSingleBracketedGroup(tokens);

    private static Dictionary<int, IReadOnlyList<TsOperandSlot>> SlotsByOffset(
        IReadOnlyList<TsTemplateToken> tokens
    ) =>
        Enumerable
            .Range(0, tokens.Count)
            .Where(i => tokens[i].Kind is TsTemplateTokenKind.Placeholder)
            .ToDictionary(i => tokens[i].Start, i => TsTemplatePlaceholderSlots.For(tokens, i));
}
