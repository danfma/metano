using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

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
    /// Maps a list of IR statements to a flat list of TS statements, splicing out
    /// <see cref="IrBlockStatement"/> since TS has no first-class nested-block node —
    /// the statement list itself is the block.
    /// </summary>
    public static IReadOnlyList<TsStatement> MapBody(
        IReadOnlyList<IrStatement> body,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        var result = new List<TsStatement>(body.Count);
        foreach (var stmt in body)
        {
            if (stmt is IrBlockStatement block)
                result.AddRange(MapBody(block.Statements, bclRegistry));
            else
                result.Add(Map(stmt, bclRegistry));
        }
        return result;
    }

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

    private static TsSwitchCase MapSwitchCase(
        IrSwitchCase c,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        // C# switch case labels are expression values; TS case tests accept
        // either a specific value or null for the default arm. An empty
        // Labels list means the default case.
        var test =
            c.Labels.Count == 0 ? null : IrToTsExpressionBridge.Map(c.Labels[0], bclRegistry);
        var body = MapBody(c.Body, bclRegistry).ToList();
        return new TsSwitchCase(test, body);
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
            IrExpressionStatement es => new TsExpressionStatement(
                IrToTsExpressionBridge.Map(es.Expression, bclRegistry)
            ),
            IrReturnStatement ret => new TsReturnStatement(
                ret.Value is not null ? IrToTsExpressionBridge.Map(ret.Value, bclRegistry) : null
            ),
            IrVariableDeclaration vd => new TsVariableDeclaration(
                vd.Name,
                vd.Initializer is not null
                    ? IrToTsExpressionBridge.Map(vd.Initializer, bclRegistry)
                    : new TsIdentifier("undefined"),
                Const: vd.IsConst
            ),
            IrIfStatement ifs => new TsIfStatement(
                IrToTsExpressionBridge.Map(ifs.Condition, bclRegistry),
                MapBody(ifs.Then, bclRegistry),
                ifs.Else is not null ? MapBody(ifs.Else, bclRegistry) : null
            ),
            IrThrowStatement th => new TsThrowStatement(
                IrToTsExpressionBridge.Map(th.Expression, bclRegistry)
            ),
            IrBlockStatement block when block.Statements.Count > 0 =>
            // Nested block appearing as a single statement: surface the first element.
            // MapBody is usually invoked by callers, which flattens blocks inline.
            Map(block.Statements[0], bclRegistry),
            IrBreakStatement => new TsExpressionStatement(new TsIdentifier("break")),
            IrContinueStatement => new TsExpressionStatement(new TsIdentifier("continue")),
            IrSwitchStatement sw => new TsSwitchStatement(
                IrToTsExpressionBridge.Map(sw.Expression, bclRegistry),
                sw.Cases.Select(c => MapSwitchCase(c, bclRegistry)).ToList()
            ),
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
