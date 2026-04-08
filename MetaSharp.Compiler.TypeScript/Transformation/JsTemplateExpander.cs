using MetaSharp.TypeScript.AST;

namespace MetaSharp.Transformation;

/// <summary>
/// Expands a JavaScript expression template against a set of transformed arguments,
/// returning a <see cref="TsLiteral"/> carrying the resulting raw JS so the printer emits
/// it verbatim at the call site.
///
/// Two flavors of placeholder are supported:
///
/// <list type="bullet">
///   <item>
///     <c>$this</c> — the optional instance receiver. Only meaningful when
///     <paramref name="receiver"/> is non-null (i.e., expanding a mapping for an instance
///     member). Replaced before the numbered placeholders.
///   </item>
///   <item>
///     <c>$0</c>, <c>$1</c>, … — the C# explicit arguments in order. Same convention as
///     <see cref="EmitAttribute"/>.
///   </item>
/// </list>
///
/// Used by both <see cref="InvocationHandler"/> (for <c>[Emit]</c>) and
/// <see cref="BclMapper"/> (for declarative <c>[MapMethod]</c>/<c>[MapProperty]</c>
/// mappings whose form is a template instead of a simple rename).
///
/// Limitation: replacement is plain string substitution, so a template referencing
/// <c>$10</c> would be partially clobbered by the replacement of <c>$1</c> first. In
/// practice, BCL methods don't reach 11+ arguments and the existing <c>[Emit]</c> usages
/// also live with this limitation.
/// </summary>
public static class JsTemplateExpander
{
    public static TsExpression Expand(
        string template,
        TsExpression? receiver,
        IReadOnlyList<TsExpression> args)
    {
        var result = template;

        if (receiver is not null)
            result = result.Replace("$this", ExpressionText(receiver));

        for (var i = 0; i < args.Count; i++)
            result = result.Replace($"${i}", ExpressionText(args[i]));

        return new TsLiteral(result);
    }

    /// <summary>
    /// Renders a transformed expression to its textual representation for inlining
    /// inside a template. Identifiers and dotted property accesses round-trip cleanly;
    /// string literals get quoted; raw <see cref="TsLiteral"/>s are spliced as-is;
    /// other shapes fall back to a placeholder comment so the failure is visible.
    /// </summary>
    private static string ExpressionText(TsExpression expr) => expr switch
    {
        TsIdentifier id => id.Name,
        TsStringLiteral str => $"\"{str.Value}\"",
        TsLiteral lit => lit.Raw,
        TsPropertyAccess access => $"{ExpressionText(access.Object)}.{access.Property}",
        _ => "/* unsupported */"
    };
}
