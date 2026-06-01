using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers a JSX-renderable object creation (<c>new T { … }</c> where
/// <c>T</c>'s <see cref="IrNamedTypeSemantics.RendersAsJsxElement"/> is true)
/// into a <see cref="TsJsxElement"/>.
/// <para>
/// Three element shapes are produced:
/// <list type="bullet">
///   <item>a native element (<c>[JsxNativeElement("div")]</c>) emits the
///   declared lowercase tag (<c>&lt;div&gt;</c>);</item>
///   <item>a component (derives from the <c>[JsxComponentBuilder]</c> base)
///   emits its capitalized type name as the tag (<c>&lt;Counter&gt;</c>),
///   resolved to its intra-project file;</item>
///   <item>an imported renderable (<c>[Import]</c>-typed, convertible to the
///   marked <c>JsxElement</c>, e.g. a <c>solid-router</c> <c>Route</c>) emits
///   its capitalized tag and carries the <c>[Import]</c> module on
///   <see cref="TsJsxElement.ExternalImports"/> so the collector resolves it to
///   the npm module (<c>&lt;Route&gt;</c> + <c>import { Route } from "solid-router"</c>).</item>
/// </list>
/// Each <see cref="IrMemberInit"/> is classified by its resolved
/// <see cref="IrMemberInit.IsChildrenSlot"/> flag: the children-collection slot
/// becomes the element's ordered children; every other assignment becomes an
/// attribute. The single source-order list on the IR keeps attribute and child
/// ordering stable (FR-010).
/// </para>
/// </summary>
public static class IrToTsJsxBridge
{
    /// <summary>
    /// The SolidJS <c>Solid.For(items, lambda)</c> helper. Recognized by its
    /// resolved origin (declaring type + member name + static-ness), not a
    /// receiver-name string, so an unrelated <c>For</c> never matches.
    /// </summary>
    private const string ForHelperType = "Metano.TypeScript.SolidJs.Solid";
    private const string ForHelperMethod = "For";

    /// <summary>The npm module the <c>&lt;For&gt;</c> helper element imports from.</summary>
    private const string SolidJsModule = "solid-js";

    /// <summary>
    /// The static builder method that produces a text/expression child
    /// (<c>Text("hi")</c> / <c>Text(count.Value)</c>), pinned to the
    /// <c>[JsxComponentBuilder]</c> base's declaring type so an unrelated
    /// user-defined static <c>Text(string)</c> (e.g. <c>Util.Text("x")</c>) is
    /// never mistaken for the JSX text builder.
    /// </summary>
    private const string TextBuilderType = "Metano.TypeScript.SolidJs.JsxComponent";
    private const string TextBuilderMethod = "Text";

    /// <summary>
    /// Converts a JSX-renderable <see cref="IrNewExpression"/> into a
    /// <see cref="TsJsxElement"/>. The caller (the expression bridge) gates on
    /// <see cref="IrNamedTypeSemantics.RendersAsJsxElement"/> before routing
    /// here.
    /// </summary>
    public static TsJsxElement Convert(IrNewExpression ne, DeclarativeMappingRegistry? bclRegistry)
    {
        var tag = ResolveTag(ne);

        var attributes = new List<TsJsxAttribute>();
        var children = new List<TsJsxChild>();

        if (ne.Initializers is { } inits)
        {
            foreach (var init in inits)
            {
                // Route on the resolved children-slot flag (set at extraction
                // when the assigned member is the element base's
                // JsxElement[]-typed collection), never on the literal member
                // name — so any binding's children slot works regardless of name.
                if (init.IsChildrenSlot)
                    children.AddRange(LowerChildren(init.Value, bclRegistry));
                else
                    attributes.Add(LowerAttribute(init, bclRegistry));
            }
        }

        // An imported renderable (e.g. a `solid-router` Route) carries its
        // `[Import]` module on the IR. Thread it onto the element so the import
        // collector resolves the capitalized tag through the external-import
        // channel (`import { Route } from "solid-router"`) instead of treating
        // it as a transpilable intra-project component (R7 / FR-022). A
        // component or native element leaves Initializers' import channel null,
        // so nothing is attached for those.
        return new TsJsxElement(
            tag,
            attributes,
            children,
            SelfClosing: children.Count == 0,
            ExternalImports: ne.ExternalImports is { Count: > 0 } ? ne.ExternalImports : null
        );
    }

    /// <summary>
    /// Single recognition entry point for every JSX-producing expression,
    /// regardless of position. Returns the lowered <see cref="TsExpression"/>
    /// (a <see cref="TsJsxElement"/> or, for a text builder, the lowered
    /// argument) when <paramref name="expression"/> is:
    /// <list type="bullet">
    ///   <item>a JSX-renderable <c>new T { … }</c> (native element, component,
    ///   or imported renderable);</item>
    ///   <item>a <c>Solid.For(...)</c> helper call → a <c>&lt;For&gt;</c> element;</item>
    ///   <item>a <c>Text("literal")</c> / <c>Text(expr)</c> builder call → the
    ///   lowered argument (a string literal or expression renderable directly).</item>
    /// </list>
    /// Returns <see langword="null"/> for any other expression. Consulted both by
    /// the expression bridge (so a bare <c>Render() =&gt; Solid.For(...)</c> return
    /// lowers to JSX instead of a raw call) and by <see cref="LowerChild"/>.
    /// </summary>
    public static TsExpression? TryLowerJsxProducer(
        IrExpression expression,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (
            expression is IrNewExpression
            {
                Type: IrNamedTypeRef { Semantics.RendersAsJsxElement: true }
            } ne
        )
            return Convert(ne, bclRegistry);

        if (TryLowerForHelper(expression, bclRegistry) is { } forElement)
            return forElement;

        if (IsTextBuilderCall(expression, out var textArg))
            return IrToTsExpressionBridge.Map(textArg!, bclRegistry);

        return null;
    }

    /// <summary>
    /// Native elements emit the declared intrinsic tag; components emit their
    /// (capitalized) type name so JSX treats the tag as a component reference.
    /// </summary>
    private static string ResolveTag(IrNewExpression ne)
    {
        if (ne.Type is IrNamedTypeRef { Semantics: { } s } named)
        {
            if (s.JsxNativeElementTag is { } nativeTag)
                return nativeTag;
            return IrToTsTypeMapper.ResolveAliasedName(named.Name);
        }

        // Fallback — should be unreachable because the caller only routes
        // named, JSX-renderable type refs here.
        return ne.Type is IrNamedTypeRef other ? other.Name : "div";
    }

    /// <summary>
    /// An attribute's name is its <see cref="IrMemberInit.EmittedName"/>
    /// (<c>[Name]</c> override, e.g. <c>ClassName → class</c>) when present, or
    /// the camelCased member name otherwise (<c>OnClick → onClick</c>). A
    /// string-literal value emits as <c>name="literal"</c>; every other lowered
    /// expression (handlers, numbers, identifiers) emits as <c>name={expr}</c>.
    /// </summary>
    private static TsJsxAttribute LowerAttribute(
        IrMemberInit init,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var name = init.EmittedName ?? TypeScriptNaming.ToCamelCaseMember(init.MemberName);
        var value = IrToTsExpressionBridge.Map(init.Value, bclRegistry);
        TsJsxAttributeValue attrValue = value switch
        {
            TsStringLiteral str => new TsJsxAttributeStringValue(str.Value),
            _ => new TsJsxAttributeExpressionValue(value),
        };
        return new TsJsxAttribute(name, attrValue);
    }

    /// <summary>
    /// Lowers the <c>Children</c> assignment value into ordered children. The
    /// value is normally a collection expression / array literal; each element
    /// becomes a child via <see cref="LowerChild"/>. A non-array value (a
    /// single child) is lowered directly.
    /// </summary>
    private static IEnumerable<TsJsxChild> LowerChildren(
        IrExpression childrenValue,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (childrenValue is IrArrayLiteral array)
        {
            foreach (var element in array.Elements)
                yield return LowerChild(element, bclRegistry);
        }
        else
        {
            yield return LowerChild(childrenValue, bclRegistry);
        }
    }

    /// <summary>
    /// Classifies a single child expression:
    /// <list type="bullet">
    ///   <item>a nested JSX-renderable <c>new</c> → <see cref="TsJsxElementChild"/>;</item>
    ///   <item><c>Text("literal")</c> → <see cref="TsJsxText"/>;</item>
    ///   <item><c>Text(expr)</c> → <see cref="TsJsxExpressionChild"/>;</item>
    ///   <item>any other expression (incl. nested non-new JSX producers) →
    ///   <see cref="TsJsxExpressionChild"/>.</item>
    /// </list>
    /// </summary>
    private static TsJsxChild LowerChild(
        IrExpression child,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        // A spread child (`..common` in `Children = [..common, …]`) renders the
        // spread source as a JSX expression child — SolidJS/JSX renders an array
        // of elements when embedded as `{common}`. Dropping the spread (the old
        // `.OfType<ExpressionElementSyntax>()` behavior) silently truncated the
        // list, so we surface it as an expression child here.
        if (child is IrSpreadExpression spread)
            return new TsJsxExpressionChild(
                IrToTsExpressionBridge.Map(spread.Expression, bclRegistry)
            );

        // A `Text("literal")` child becomes a JSX text node; `Text(expr)` becomes
        // an expression child. Handled here (not via TryLowerJsxProducer) because
        // only the child position cares about the literal-vs-expression
        // distinction that selects TsJsxText.
        if (IsTextBuilderCall(child, out var textArg))
        {
            var loweredArg = IrToTsExpressionBridge.Map(textArg!, bclRegistry);
            return loweredArg is TsStringLiteral str
                ? new TsJsxText(str.Value)
                : new TsJsxExpressionChild(loweredArg);
        }

        // JSX-renderable `new` and `Solid.For(...)` lower to nested elements.
        if (TryLowerJsxProducer(child, bclRegistry) is TsJsxElement element)
            return new TsJsxElementChild(element);

        return new TsJsxExpressionChild(IrToTsExpressionBridge.Map(child, bclRegistry));
    }

    /// <summary>
    /// Lowers a <c>Solid.For(items, (item, index) =&gt; element)</c> call into
    /// the SolidJS <c>&lt;For each={items}&gt;{lambda}&lt;/For&gt;</c> element
    /// (D7 / R5). The first argument becomes the <c>each</c> attribute; the
    /// render lambda is lowered to a single expression child (its body returns
    /// JSX through the normal JSX-renderable <c>new</c> routing). The
    /// <c>For</c> import is threaded on the element so the collector resolves it
    /// to <c>solid-js</c> instead of mistaking it for a transpilable component.
    /// Returns <see langword="null"/> when the call is not the <c>For</c> helper.
    /// </summary>
    private static TsJsxElement? TryLowerForHelper(
        IrExpression expression,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        if (
            expression
            is not IrCallExpression
            {
                Origin:
                {
                    DeclaringTypeFullName: ForHelperType,
                    MemberName: ForHelperMethod,
                    IsStatic: true,
                },
                Arguments: { Count: 2 } args,
            }
        )
            return null;

        var each = IrToTsExpressionBridge.Map(args[0].Value, bclRegistry);
        var renderProp = StripRenderPropParameterTypes(
            IrToTsExpressionBridge.Map(args[1].Value, bclRegistry)
        );
        return new TsJsxElement(
            "For",
            [new TsJsxAttribute("each", new TsJsxAttributeExpressionValue(each))],
            [new TsJsxExpressionChild(renderProp)],
            SelfClosing: false,
            ExternalImports:
            [
                new IrExternalImport("For", SolidJsModule, IsDefault: false, Version: null),
            ]
        );
    }

    /// <summary>
    /// Strips parameter type annotations from a <c>&lt;For&gt;</c> render-prop
    /// arrow. Solid's <c>&lt;For&gt;</c> callback is
    /// <c>(item: T, index: Accessor&lt;number&gt;) =&gt; Element</c>; the C#
    /// lambda types <c>index</c> as <c>int</c>, which would clash with the
    /// <c>Accessor&lt;number&gt;</c> contextual type (TS2322). Dropping the
    /// annotations lets Solid contextually type both params (R5 / D7). Any
    /// non-arrow expression is returned unchanged.
    /// </summary>
    private static TsExpression StripRenderPropParameterTypes(TsExpression renderProp) =>
        renderProp is TsArrowFunction arrow
            ? arrow with
            {
                Parameters = arrow.Parameters.Select(p => p with { Type = null }).ToList(),
            }
            : renderProp;

    /// <summary>
    /// True when <paramref name="expression"/> is a call to the static
    /// <c>Text(...)</c> builder on the <c>[JsxComponentBuilder]</c> base. The
    /// single argument is returned via <paramref name="argument"/>. The match
    /// keys on the resolved origin (declaring type + member name + static-ness)
    /// rather than a receiver-name string, so neither a shadowed instance
    /// <c>Text</c> nor an unrelated static <c>Text(string)</c> on another type
    /// matches.
    /// </summary>
    private static bool IsTextBuilderCall(IrExpression expression, out IrExpression? argument)
    {
        argument = null;
        if (
            expression is IrCallExpression
            {
                Origin:
                {
                    DeclaringTypeFullName: TextBuilderType,
                    MemberName: TextBuilderMethod,
                    IsStatic: true,
                },
                Arguments: { Count: 1 } args,
            }
        )
        {
            argument = args[0].Value;
            return true;
        }
        return false;
    }
}
