using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers a JSX component record (an <see cref="IrClassDeclaration"/> whose
/// <see cref="IrTypeSemantics.IsJsxComponent"/> is true) into a SolidJS-style
/// TSX function component:
/// <list type="number">
///   <item>an <c>export type &lt;Name&gt;Props = { … }</c> alias with one
///   optional member per settable property (<c>init</c>/<c>set</c>) and record
///   positional parameter (computed / get-only / <c>[Ignore]</c> members
///   excluded);</item>
///   <item>an <c>export function &lt;Name&gt;(props: &lt;Name&gt;Props)</c>
///   whose body is the lowered <c>Render()</c> body;</item>
///   <item>for every prop referenced in the body, a hoisted
///   <c>const props$&lt;camel&gt; = props.&lt;camel&gt; ?? &lt;default&gt;;</c>
///   with the referencing <c>this.&lt;Prop&gt;</c> reads rewritten to that
///   local.</item>
/// </list>
/// A JSX component never emits a class, <c>equals</c>/<c>hashCode</c>/<c>with</c>,
/// or constructor — it is purely a function + props type.
/// </summary>
public static class IrToTsJsxComponentBridge
{
    /// <summary>The C# render entry method on the <c>[JsxComponentBuilder]</c> base.</summary>
    private const string RenderMethod = "Render";

    /// <summary>Prefix for hoisted prop locals (<c>props$count</c>).</summary>
    private const string HoistPrefix = "props$";

    /// <summary>
    /// Emits the props type alias + function component for
    /// <paramref name="ir"/> into <paramref name="sink"/>.
    /// <paramref name="componentName"/> is the resolved TS name (honoring
    /// <c>[Name]</c>); the props type is <c>&lt;componentName&gt;Props</c> and
    /// the single parameter is <c>props: &lt;componentName&gt;Props</c>.
    /// </summary>
    public static void Convert(
        IrClassDeclaration ir,
        string componentName,
        List<TsTopLevel> sink,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        var propsTypeName = componentName + "Props";

        var props = CollectProps(ir);

        // T016 — props type alias. Every member is optional.
        var fields = props
            .Select(p => new TsObjectTypeField(
                TypeScriptNaming.ToCamelCaseMember(p.SourceName),
                IrToTsTypeMapper.Map(p.Type),
                Optional: true
            ))
            .ToList();
        sink.Add(new TsTypeAlias(propsTypeName, new TsObjectType(fields)));

        // T017/T018 — function body: render body lowered, prop reads rewritten,
        // referenced props hoisted.
        var renderBody = FindRenderBody(ir);
        var (rewrittenBody, referenced) = RewritePropReferences(renderBody, props);

        var statements = new List<TsStatement>();
        foreach (var prop in props)
        {
            if (!referenced.Contains(prop.SourceName))
                continue;
            statements.Add(BuildHoist(prop, bclRegistry));
        }
        statements.AddRange(IrToTsStatementBridge.MapBody(rewrittenBody, bclRegistry));

        sink.Add(
            new TsFunction(
                componentName,
                [new TsParameter("props", new TsNamedType(propsTypeName))],
                ReturnType: null,
                statements
            )
        );
    }

    /// <summary>
    /// A prop sourced either from a settable property or a record positional
    /// parameter. <see cref="SourceName"/> is the original C# name (used for
    /// camelCasing, the hoist local, and the <c>this.&lt;Name&gt;</c> rewrite
    /// match); <see cref="Initializer"/> carries an explicit
    /// <c>= value</c> default when present.
    /// </summary>
    private sealed record PropInfo(string SourceName, IrTypeRef Type, IrExpression? Initializer);

    /// <summary>
    /// Collects component props: every non-static settable property
    /// (<c>GetSet</c>/<c>GetInit</c>) that is not computed (no getter body),
    /// not <c>[Ignore]</c>, plus every record positional parameter. Order is
    /// declaration order (properties first, then positional parameters) — the
    /// golden tests pin it.
    /// </summary>
    private static List<PropInfo> CollectProps(IrClassDeclaration ir)
    {
        var props = new List<PropInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (ir.Members is { } members)
        {
            foreach (var member in members)
            {
                if (member is not IrPropertyDeclaration prop)
                    continue;
                if (prop.IsStatic)
                    continue;
                if (
                    prop.Accessors
                    is not (IrPropertyAccessors.GetSet or IrPropertyAccessors.GetInit)
                )
                    continue;
                // Computed/expression-bodied getters are not settable state.
                if (prop.Semantics is { HasGetterBody: true })
                    continue;
                if (HasIgnore(prop.Attributes))
                    continue;
                if (seen.Add(prop.Name))
                    props.Add(new PropInfo(prop.Name, prop.Type, prop.Initializer));
            }
        }

        // Record positional parameters become props as well (FR-003).
        if (ir.Constructor is { } ctor)
        {
            foreach (var ctorParam in ctor.Parameters)
            {
                if (ctorParam.Promotion == IrParameterPromotion.None)
                    continue;
                var param = ctorParam.Parameter;
                if (seen.Add(param.Name))
                    props.Add(new PropInfo(param.Name, param.Type, param.DefaultValue));
            }
        }

        return props;
    }

    private static bool HasIgnore(IReadOnlyList<IrAttribute>? attributes) =>
        attributes is not null
        && attributes.Any(a => string.Equals(a.Name, "Ignore", StringComparison.Ordinal));

    /// <summary>
    /// Finds the overridden render method's body. Matches the method named
    /// <c>Render</c> (the <c>[JsxComponentBuilder]</c> entry that returns the
    /// marked element type); returns an empty body when absent so the function
    /// still emits.
    /// </summary>
    private static IReadOnlyList<IrStatement> FindRenderBody(IrClassDeclaration ir)
    {
        if (ir.Members is not { } members)
            return [];
        foreach (var member in members)
        {
            if (
                member is IrMethodDeclaration { Name: RenderMethod, Body: { } body }
                && !member.IsStatic
            )
                return body;
        }
        return [];
    }

    /// <summary>
    /// Builds the hoisted <c>const props$&lt;camel&gt; = props.&lt;camel&gt; ?? &lt;default&gt;;</c>
    /// for a referenced prop. The default is the prop's explicit initializer
    /// when present, otherwise the C# type default (numeric → <c>0</c>, bool →
    /// <c>false</c>, everything else → <c>null</c>).
    /// </summary>
    private static TsStatement BuildHoist(PropInfo prop, DeclarativeMappingRegistry? bclRegistry)
    {
        var camel = TypeScriptNaming.ToCamelCaseMember(prop.SourceName);
        var local = HoistPrefix + camel;
        var propsAccess = new TsPropertyAccess(new TsIdentifier("props"), camel);
        var defaultValue = prop.Initializer is not null
            ? IrToTsExpressionBridge.Map(prop.Initializer, bclRegistry)
            : DefaultForType(prop.Type);
        var initializer = new TsBinaryExpression(propsAccess, "??", defaultValue);
        return new TsVariableDeclaration(local, initializer, Const: true);
    }

    /// <summary>
    /// The C# <c>default(T)</c> literal for a prop type: <c>0</c> for numeric
    /// primitives, <c>false</c> for booleans, and <c>null</c> for strings,
    /// reference types, and nullable types (matching the
    /// <c>T | null = null</c> convention).
    /// </summary>
    private static TsExpression DefaultForType(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef { Primitive: IrPrimitive.Boolean } => new TsLiteral("false"),
            IrPrimitiveTypeRef p when IsNumericPrimitive(p.Primitive) => new TsLiteral("0"),
            _ => new TsLiteral("null"),
        };

    private static bool IsNumericPrimitive(IrPrimitive primitive) =>
        primitive
            is IrPrimitive.Byte
                or IrPrimitive.Int16
                or IrPrimitive.Int32
                or IrPrimitive.Int64
                or IrPrimitive.Float32
                or IrPrimitive.Float64
                or IrPrimitive.Decimal;

    // ─── Prop-reference rewrite ─────────────────────────────────────────────
    //
    // A read of a prop inside the render body is `this.<Prop>` (the extractor
    // promotes the implicit-this shorthand). Each such read is rewritten to the
    // hoisted local `props$<camel>` and the prop is recorded as referenced so
    // its hoist is emitted. The walker recurses through every body node — most
    // importantly `IrNewExpression.Initializers` (JSX attributes + children)
    // and lambda bodies (event handlers) — so a prop read nested in JSX or a
    // handler is rewritten too.

    private static (
        IReadOnlyList<IrStatement> Body,
        HashSet<string> Referenced
    ) RewritePropReferences(IReadOnlyList<IrStatement> body, IReadOnlyList<PropInfo> props)
    {
        var propNames = new HashSet<string>(
            props.Select(p => p.SourceName),
            StringComparer.Ordinal
        );
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var rewritten = body.Select(s => RewriteStatement(s, propNames, referenced)).ToList();
        return (rewritten, referenced);
    }

    private static IReadOnlyList<IrStatement> RewriteList(
        IReadOnlyList<IrStatement> body,
        HashSet<string> propNames,
        HashSet<string> referenced
    ) => body.Select(s => RewriteStatement(s, propNames, referenced)).ToList();

    private static IrStatement RewriteStatement(
        IrStatement stmt,
        HashSet<string> propNames,
        HashSet<string> referenced
    ) =>
        stmt switch
        {
            IrExpressionStatement es => new IrExpressionStatement(
                Rewrite(es.Expression, propNames, referenced)
            ),
            IrReturnStatement ret => new IrReturnStatement(
                ret.Value is null ? null : Rewrite(ret.Value, propNames, referenced)
            ),
            IrVariableDeclaration vd => vd with
            {
                Initializer = vd.Initializer is null
                    ? null
                    : Rewrite(vd.Initializer, propNames, referenced),
            },
            // A deconstructing declaration (`var (count, setCount) =
            // Solid.CreateSignal(Count)`) holds a prop read in its initializer;
            // descend so `this.Count` is rewritten to the hoisted local. The
            // bound element names are fresh locals, never prop reads.
            IrTupleDeconstruction td => td with
            {
                Initializer = Rewrite(td.Initializer, propNames, referenced),
            },
            IrIfStatement ifs => new IrIfStatement(
                Rewrite(ifs.Condition, propNames, referenced),
                RewriteList(ifs.Then, propNames, referenced),
                ifs.Else is null ? null : RewriteList(ifs.Else, propNames, referenced)
            ),
            IrThrowStatement th => new IrThrowStatement(
                Rewrite(th.Expression, propNames, referenced)
            ),
            IrForEachStatement fe => fe with
            {
                Collection = Rewrite(fe.Collection, propNames, referenced),
                Body = RewriteList(fe.Body, propNames, referenced),
            },
            IrForStatement fs => fs with
            {
                Initializer = fs.Initializer is null
                    ? null
                    : RewriteStatement(fs.Initializer, propNames, referenced),
                Condition = fs.Condition is null
                    ? null
                    : Rewrite(fs.Condition, propNames, referenced),
                Increment = fs.Increment is null
                    ? null
                    : Rewrite(fs.Increment, propNames, referenced),
                Body = RewriteList(fs.Body, propNames, referenced),
            },
            IrWhileStatement ws => new IrWhileStatement(
                Rewrite(ws.Condition, propNames, referenced),
                RewriteList(ws.Body, propNames, referenced)
            ),
            IrDoWhileStatement dw => new IrDoWhileStatement(
                RewriteList(dw.Body, propNames, referenced),
                Rewrite(dw.Condition, propNames, referenced)
            ),
            IrBlockStatement block => new IrBlockStatement(
                RewriteList(block.Statements, propNames, referenced)
            ),
            _ => stmt,
        };

    private static IrExpression Rewrite(
        IrExpression expr,
        HashSet<string> propNames,
        HashSet<string> referenced
    )
    {
        // The rewrite target: a prop read through the implicit-this shorthand.
        if (
            expr is IrMemberAccess { Target: IrThisExpression, MemberName: var name }
            && propNames.Contains(name)
        )
        {
            referenced.Add(name);
            return new IrIdentifier(HoistPrefix + TypeScriptNaming.ToCamelCaseMember(name));
        }

        return expr switch
        {
            IrMemberAccess ma => ma with { Target = Rewrite(ma.Target, propNames, referenced) },
            IrElementAccess ea => ea with
            {
                Target = Rewrite(ea.Target, propNames, referenced),
                Index = Rewrite(ea.Index, propNames, referenced),
            },
            IrOptionalChain oc => oc with { Target = Rewrite(oc.Target, propNames, referenced) },
            IrCallExpression call => call with
            {
                Target = Rewrite(call.Target, propNames, referenced),
                Arguments = call
                    .Arguments.Select(a =>
                        a with
                        {
                            Value = Rewrite(a.Value, propNames, referenced),
                        }
                    )
                    .ToList(),
            },
            IrNewExpression ne => ne with
            {
                Arguments = ne
                    .Arguments.Select(a =>
                        a with
                        {
                            Value = Rewrite(a.Value, propNames, referenced),
                        }
                    )
                    .ToList(),
                Initializers = ne.Initializers is null
                    ? null
                    : ne
                        .Initializers.Select(i =>
                            i with
                            {
                                Value = Rewrite(i.Value, propNames, referenced),
                            }
                        )
                        .ToList(),
            },
            IrBinaryExpression bin => bin with
            {
                Left = Rewrite(bin.Left, propNames, referenced),
                Right = Rewrite(bin.Right, propNames, referenced),
            },
            IrUnaryExpression un => un with
            {
                Operand = Rewrite(un.Operand, propNames, referenced),
            },
            IrConditionalExpression cond => cond with
            {
                Condition = Rewrite(cond.Condition, propNames, referenced),
                WhenTrue = Rewrite(cond.WhenTrue, propNames, referenced),
                WhenFalse = Rewrite(cond.WhenFalse, propNames, referenced),
            },
            IrCastExpression cast => cast with
            {
                Expression = Rewrite(cast.Expression, propNames, referenced),
            },
            IrAwaitExpression aw => aw with
            {
                Expression = Rewrite(aw.Expression, propNames, referenced),
            },
            IrThrowExpression th => th with
            {
                Expression = Rewrite(th.Expression, propNames, referenced),
            },
            IrArrayLiteral arr => arr with
            {
                Elements = arr.Elements.Select(e => Rewrite(e, propNames, referenced)).ToList(),
            },
            IrSpreadExpression spread => spread with
            {
                Expression = Rewrite(spread.Expression, propNames, referenced),
            },
            IrLambdaExpression lambda => lambda with
            {
                Body = RewriteList(lambda.Body, propNames, referenced),
            },
            IrWithExpression w => w with
            {
                Source = Rewrite(w.Source, propNames, referenced),
                Assignments = w
                    .Assignments.Select(a =>
                        a with
                        {
                            Value = Rewrite(a.Value, propNames, referenced),
                        }
                    )
                    .ToList(),
            },
            IrStringInterpolation interp => interp with
            {
                Parts = interp
                    .Parts.Select(p =>
                        p is IrInterpolationExpression ie
                            ? ie with
                            {
                                Expression = Rewrite(ie.Expression, propNames, referenced),
                            }
                            : p
                    )
                    .ToList(),
            },
            // [Emit]/[Import] call sites (e.g. `Solid.CreateSignal(Count)`)
            // lower to a template during extraction; a prop read survives as a
            // template argument / receiver, so the rewrite must descend here
            // too — otherwise `this.Count` leaks past the hoist (checklist #5).
            IrTemplateExpression tpl => tpl with
            {
                Receiver = tpl.Receiver is null
                    ? null
                    : Rewrite(tpl.Receiver, propNames, referenced),
                Arguments = tpl.Arguments.Select(a => Rewrite(a, propNames, referenced)).ToList(),
            },
            _ => expr,
        };
    }
}
