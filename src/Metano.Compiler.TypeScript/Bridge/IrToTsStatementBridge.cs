using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Converts target-agnostic <see cref="IrStatement"/> nodes into TypeScript AST
/// statements. Uses <see cref="IrToTsExpressionBridge"/> for the expression side.
/// Callers can pass a <see cref="DeclarativeMappingRegistry"/> to have BCL
/// mappings applied to member accesses and calls; without it the expressions
/// are emitted raw.
/// </summary>
public static class IrToTsStatementBridge
{
    /// <summary>
    /// Maps a list of IR statements to TS statements. A nested
    /// <see cref="IrBlockStatement"/> stays a <c>{ … }</c> block so the
    /// <c>let</c>/<c>const</c> bindings it declares keep their C# scope (a bare
    /// C# block, or a loop wrapped with its hoisted <c>out var</c> locals).
    /// </summary>
    public static IReadOnlyList<TsStatement> MapBody(
        IReadOnlyList<IrStatement> body,
        DeclarativeMappingRegistry? bclRegistry = null
    ) => body.Select(stmt => Map(stmt, bclRegistry)).ToList();

    private static TsStatement MapForEach(
        IrForEachStatement fe,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        // C# `foreach (var x in xs) { … }` → TS `for (const x of xs) { … }`
        var collection = IrToTsExpressionBridge.Map(fe.Collection, bclRegistry);
        var body = MapBody(fe.Body, bclRegistry);
        var header = $"for (const {fe.Variable} of {PrintExpression(collection)}) {{";
        return new TsRawStatement(WrapLoop(header, body));
    }

    private static TsStatement MapFor(IrForStatement fs, DeclarativeMappingRegistry? bclRegistry)
    {
        var init = fs.Initializer is null
            ? string.Empty
            : RenderFragmentStatement(fs.Initializer, bclRegistry);
        var cond = fs.Condition is null
            ? string.Empty
            : PrintExpression(IrToTsExpressionBridge.Map(fs.Condition, bclRegistry));
        var inc = fs.Increment is null
            ? string.Empty
            : PrintExpression(IrToTsExpressionBridge.Map(fs.Increment, bclRegistry));
        var body = MapBody(fs.Body, bclRegistry);
        var header = $"for ({init}; {cond}; {inc}) {{";
        return new TsRawStatement(WrapLoop(header, body));
    }

    private static TsStatement MapWhile(
        IrWhileStatement ws,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var cond = PrintExpression(IrToTsExpressionBridge.Map(ws.Condition, bclRegistry));
        return new TsRawStatement(WrapLoop($"while ({cond}) {{", MapBody(ws.Body, bclRegistry)));
    }

    private static TsStatement MapDoWhile(
        IrDoWhileStatement dw,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var cond = PrintExpression(IrToTsExpressionBridge.Map(dw.Condition, bclRegistry));
        var body = MapBody(dw.Body, bclRegistry);
        var inner = RenderIndentedBody(body);
        return new TsRawStatement($"do {{\n{inner}}} while ({cond});");
    }

    private static TsStatement MapTry(IrTryStatement ts, DeclarativeMappingRegistry? bclRegistry)
    {
        var body = MapBody(ts.Body, bclRegistry);
        var sb = new System.Text.StringBuilder();
        sb.Append("try {\n").Append(RenderIndentedBody(body)).Append('}');
        if (ts.Catches is not null)
        {
            foreach (var c in ts.Catches)
            {
                var varName = c.VariableName ?? "_err";
                sb.Append(" catch (").Append(varName).Append(") {\n");
                sb.Append(RenderIndentedBody(MapBody(c.Body, bclRegistry))).Append('}');
            }
        }
        if (ts.Finally is not null)
        {
            sb.Append(" finally {\n");
            sb.Append(RenderIndentedBody(MapBody(ts.Finally, bclRegistry))).Append('}');
        }
        return new TsRawStatement(sb.ToString());
    }

    private static string RenderFragmentStatement(
        IrStatement stmt,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        stmt switch
        {
            IrVariableDeclaration vd when vd.Initializer is not null =>
                $"let {vd.Name} = {PrintExpression(IrToTsExpressionBridge.Map(vd.Initializer, bclRegistry))}",
            IrExpressionStatement es => PrintExpression(
                IrToTsExpressionBridge.Map(es.Expression, bclRegistry)
            ),
            _ => "/* TODO: fragment */",
        };

    private static string WrapLoop(string header, IReadOnlyList<TsStatement> body) =>
        header + "\n" + RenderIndentedBody(body) + "}";

    private static string RenderIndentedBody(IReadOnlyList<TsStatement> body)
    {
        var file = new TsSourceFile(
            "frag.ts",
            [new TsFunction("frag", [], new TsVoidType(), body)]
        );
        var printed = new Printer().Print(file);
        var start = printed.IndexOf('{') + 1;
        var end = printed.LastIndexOf('}');
        var inner = printed[start..end].Trim('\n', '\r');
        if (string.IsNullOrWhiteSpace(inner))
            return "";
        var lines = inner.Split('\n');
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
            {
                sb.AppendLine();
                continue;
            }
            // Lines are already indented by one level inside the synthetic
            // function; keep that indent and add a newline.
            sb.Append(trimmed).AppendLine();
        }
        return sb.ToString();
    }

    private static string PrintExpression(TsExpression expr)
    {
        var stmt = new TsRawStatement("__PLACEHOLDER__");
        var file = new TsSourceFile(
            "frag.ts",
            [new TsFunction("frag", [], new TsVoidType(), [new TsExpressionStatement(expr)])]
        );
        var printed = new Printer().Print(file);
        // Extract the expression statement from inside the function body.
        var start = printed.IndexOf('{') + 1;
        var end = printed.LastIndexOf('}');
        var inner = printed[start..end].Trim();
        return inner.TrimEnd(';');
    }

    /// <summary>
    /// A JS <c>switch</c> compares with <c>===</c>, so a <c>case double.NaN:</c>
    /// label would never match. When a NaN label is present the switch is
    /// rewritten to <c>switch (true)</c> with one boolean test per label. That
    /// re-reads the subject in every test, so it is only done when the subject is
    /// an identifier, <c>this</c>, or a member-access chain on one (a property
    /// getter in that chain runs once per test); any other subject gets a TODO
    /// label that fails to compile instead of a case that silently never matches.
    /// </summary>
    private static TsSwitchStatement MapSwitch(
        IrSwitchStatement sw,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var loweredSubject = IrToTsExpressionBridge.Map(sw.Expression, bclRegistry);

        if (!HasNaNCaseLabel(sw))
        {
            return new TsSwitchStatement(
                loweredSubject,
                MapSwitchCases(sw, LowerLabelAsConstant, bclRegistry)
            );
        }

        if (!IsSafeToReread(sw.Expression))
        {
            return new TsSwitchStatement(
                loweredSubject,
                MapSwitchCases(sw, LowerNaNLabelAsUnsupported, bclRegistry)
            );
        }

        return new TsSwitchStatement(
            new TsLiteral("true"),
            MapSwitchCases(sw, LowerLabelAsBooleanTest, bclRegistry)
        );

        TsExpression LowerLabelAsConstant(IrExpression label) =>
            IrToTsExpressionBridge.Map(label, bclRegistry);

        TsExpression LowerLabelAsBooleanTest(IrExpression label) =>
            IrToTsExpressionBridge.BuildCaseLabelTest(loweredSubject, label, bclRegistry);

        TsExpression LowerNaNLabelAsUnsupported(IrExpression label) =>
            IrToTsExpressionBridge.IsNaNConstant(label)
                ? new TsIdentifier(
                    "/* TODO: NaN case label needs an identifier or member-access switch subject */"
                )
                : LowerLabelAsConstant(label);
    }

    private static bool HasNaNCaseLabel(IrSwitchStatement sw) =>
        sw.Cases.Any(c => c.Labels.Any(IrToTsExpressionBridge.IsNaNConstant));

    private static bool IsSafeToReread(IrExpression subject) =>
        subject switch
        {
            IrIdentifier or IrThisExpression => true,
            IrMemberAccess access => IsSafeToReread(access.Target),
            _ => false,
        };

    private static List<TsSwitchCase> MapSwitchCases(
        IrSwitchStatement sw,
        Func<IrExpression, TsExpression> lowerLabel,
        DeclarativeMappingRegistry? bclRegistry
    ) => sw.Cases.SelectMany(c => MapSwitchCase(c, lowerLabel, bclRegistry)).ToList();

    private static IEnumerable<TsSwitchCase> MapSwitchCase(
        IrSwitchCase c,
        Func<IrExpression, TsExpression> lowerLabel,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var body = MapBody(c.Body, bclRegistry).ToList();

        // Default (no labels).
        if (c.Labels.Count == 0)
        {
            yield return new TsSwitchCase(null, body);
            yield break;
        }

        // C# allows multiple labels to share one body (`case 1: case 2: …`).
        // TS expresses this as consecutive empty-body cases with the body
        // attached to the last label so execution falls through to it.
        for (var i = 0; i < c.Labels.Count; i++)
        {
            var test = lowerLabel(c.Labels[i]);
            yield return new TsSwitchCase(
                test,
                i == c.Labels.Count - 1 ? body : Array.Empty<TsStatement>()
            );
        }
    }

    public static TsStatement Map(
        IrStatement statement,
        DeclarativeMappingRegistry? bclRegistry = null
    ) =>
        statement switch
        {
            // Yield return surfaces as IrExpressionStatement(IrYieldExpression) —
            // peel it out and emit the dedicated TsYieldStatement.
            IrExpressionStatement { Expression: IrYieldExpression ye } => new TsYieldStatement(
                ye.Value is not null
                    ? IrToTsExpressionBridge.Map(ye.Value, bclRegistry)
                    : new TsIdentifier("undefined")
            ),
            IrExpressionStatement { Expression: IrNullConditionalAccess access } =>
                IrToTsExpressionBridge.MapNullConditionalStatement(access, bclRegistry),
            IrExpressionStatement es => new TsExpressionStatement(
                IrToTsExpressionBridge.Map(es.Expression, bclRegistry)
            ),
            IrReturnStatement ret => new TsReturnStatement(
                ret.Value is not null ? IrToTsExpressionBridge.Map(ret.Value, bclRegistry) : null
            ),
            // Declared ahead of its first assignment (an `out var` local, or
            // `T x;`): a typed, uninitialized `let x: T;` keeps TypeScript's
            // definite-assignment analysis in charge instead of `undefined`.
            IrVariableDeclaration { Initializer: null, Type: { } type } vd =>
                new TsVariableDeclaration(
                    vd.Name,
                    Initializer: null,
                    Const: false,
                    Type: IrToTsTypeMapper.Map(type)
                ),
            IrVariableDeclaration vd => new TsVariableDeclaration(
                vd.Name,
                vd.Initializer is not null
                    ? IrToTsExpressionBridge.Map(vd.Initializer, bclRegistry)
                    : new TsIdentifier("undefined"),
                Const: vd.IsConst
            ),
            IrTupleDeconstruction td => new TsDestructuringDeclaration(
                td.Elements.Select(e => e.Name).ToList(),
                td.Initializer is IrOutArgumentCall outCall
                    ? IrToTsExpressionBridge.MapOutArgumentCallValue(outCall, bclRegistry)
                    : IrToTsExpressionBridge.Map(td.Initializer, bclRegistry),
                Const: td.IsConst
            ),
            IrIfStatement ifs => new TsIfStatement(
                IrToTsExpressionBridge.Map(ifs.Condition, bclRegistry),
                MapBody(ifs.Then, bclRegistry),
                ifs.Else is not null ? MapBody(ifs.Else, bclRegistry) : null
            ),
            IrThrowStatement th => new TsThrowStatement(
                IrToTsExpressionBridge.Map(th.Expression, bclRegistry)
            ),
            IrBlockStatement block => new TsBlockStatement(MapBody(block.Statements, bclRegistry)),
            IrBreakStatement => new TsExpressionStatement(new TsIdentifier("break")),
            IrContinueStatement => new TsExpressionStatement(new TsIdentifier("continue")),
            IrSwitchStatement sw => MapSwitch(sw, bclRegistry),
            IrYieldBreakStatement => new TsYieldBreakStatement(),
            IrForEachStatement fe => MapForEach(fe, bclRegistry),
            IrForStatement fs => MapFor(fs, bclRegistry),
            IrWhileStatement ws => MapWhile(ws, bclRegistry),
            IrDoWhileStatement dw => MapDoWhile(dw, bclRegistry),
            IrTryStatement ts => MapTry(ts, bclRegistry),
            IrUnsupportedStatement u => new TsExpressionStatement(
                new TsIdentifier($"/* TODO: unsupported IR statement {u.Kind} */")
            ),
            _ => new TsExpressionStatement(
                new TsIdentifier($"/* TODO: {statement.GetType().Name} */")
            ),
        };
}
