using Metano.Compiler.Analysis;
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
    internal static IReadOnlyList<TsStatement> MapBody(
        IReadOnlyList<IrStatement> body,
        IrToTsBridgeContext context
    ) => body.Select(stmt => Map(stmt, context)).ToList();

    private static TsStatement MapForEach(IrForEachStatement fe, IrToTsBridgeContext context)
    {
        // C# `foreach (var x in xs) { … }` → TS `for (const x of xs) { … }`
        var collection = IrToTsExpressionBridge.Map(fe.Collection, context);
        var body = MapBody(fe.Body, context);
        var header = $"for (const {fe.Variable} of {PrintExpression(collection)}) {{";

        return new TsRawStatement(WrapLoop(header, body))
        {
            EmbeddedStatements = body,
            EmbeddedExpressions = [collection],
        };
    }

    private static TsStatement MapFor(IrForStatement fs, IrToTsBridgeContext context)
    {
        var initializers = fs.Initializers.Select(i => MapForInitializer(i, context)).ToList();

        var condition = fs.Condition is null
            ? null
            : IrToTsExpressionBridge.Map(fs.Condition, context);

        var incrementors = fs
            .Incrementors.Select(i => IrToTsExpressionBridge.Map(i, context))
            .ToList();

        var body = MapBody(fs.Body, context);
        var conditionText = condition is null ? string.Empty : PrintExpression(condition);

        var header =
            $"for ({RenderForInitializers(initializers)}; {conditionText}; {RenderCommaList(incrementors)}) {{";

        return new TsRawStatement(WrapLoop(header, body))
        {
            EmbeddedStatements = [.. initializers, .. body],
            EmbeddedExpressions =
            [
                .. condition is null ? [] : new[] { condition },
                .. incrementors,
            ],
        };
    }

    /// <summary>
    /// A declaration maps as a statement (keeping its type and initializer
    /// for the import walk); an initializer expression maps as a plain
    /// expression, since a header has no room for the statement forms some
    /// expressions take (a null-conditional call lowers to an <c>if</c>). Only
    /// plain declarations reach a header, and those always map to a
    /// <see cref="TsVariableDeclaration"/>.
    /// </summary>
    private static TsStatement MapForInitializer(
        IrStatement initializer,
        IrToTsBridgeContext context
    ) =>
        initializer is IrExpressionStatement expression
            ? new TsExpressionStatement(IrToTsExpressionBridge.Map(expression.Expression, context))
            : Map(initializer, context);

    /// <summary>
    /// The initializer section of a <c>for</c> header: the declarators of
    /// <c>int i = 0, j = n</c> share one <c>let</c> (<c>let i = 0, j = n</c>);
    /// initializer expressions are comma-separated (<c>i = 0, j = n</c>).
    /// The extractor yields either declarations only or expressions only.
    /// </summary>
    private static string RenderForInitializers(IReadOnlyList<TsStatement> initializers)
    {
        if (initializers.Count == 0)
            return string.Empty;

        if (initializers.All(s => s is TsVariableDeclaration))
        {
            var declarators = initializers.Cast<TsVariableDeclaration>().Select(RenderDeclarator);

            return "let " + string.Join(", ", declarators);
        }

        return string.Join(", ", initializers.Select(RenderInitializerExpression));
    }

    /// <summary>
    /// One declarator of a <c>let</c> list: <c>name = value</c>, or
    /// <c>name: T</c> for a typed declaration without an initializer.
    /// </summary>
    private static string RenderDeclarator(TsVariableDeclaration declaration) =>
        declaration switch
        {
            { Type: { } type, Initializer: null } =>
                $"{declaration.Name}{DefiniteAssignmentMark(declaration)}: {Printer.RenderType(type)}",
            { Initializer: { } value } => $"{declaration.Name} = {PrintExpression(value)}",
            _ => declaration.Name,
        };

    private static string DefiniteAssignmentMark(TsVariableDeclaration declaration) =>
        declaration.DefinitelyAssigned ? "!" : string.Empty;

    /// <summary>
    /// An initializer expression of a <c>for</c> header. Any other statement
    /// cannot reach a header: the extractor only produces declarations or
    /// expression statements there, and never mixes them.
    /// </summary>
    private static string RenderInitializerExpression(TsStatement statement) =>
        statement is TsExpressionStatement expression
            ? PrintExpression(expression.Expression)
            : throw new System.Diagnostics.UnreachableException(
                $"A for header cannot hold a {statement.GetType().Name}."
            );

    /// <summary>
    /// Comma-separated expressions (<c>i++, j--</c>). The comma operator binds
    /// looser than every other JS operator, assignment and arrow functions
    /// included, and the IR has no sequence expression that would print a
    /// bare comma, so no operand needs parentheses.
    /// </summary>
    private static string RenderCommaList(IReadOnlyList<TsExpression> expressions) =>
        string.Join(", ", expressions.Select(PrintExpression));

    private static TsStatement MapWhile(IrWhileStatement ws, IrToTsBridgeContext context)
    {
        var condition = IrToTsExpressionBridge.Map(ws.Condition, context);
        var body = MapBody(ws.Body, context);

        return new TsRawStatement(WrapLoop($"while ({PrintExpression(condition)}) {{", body))
        {
            EmbeddedStatements = body,
            EmbeddedExpressions = [condition],
        };
    }

    private static TsStatement MapDoWhile(IrDoWhileStatement dw, IrToTsBridgeContext context)
    {
        var condition = IrToTsExpressionBridge.Map(dw.Condition, context);
        var body = MapBody(dw.Body, context);
        var inner = RenderIndentedBody(body);

        return new TsRawStatement($"do {{\n{inner}}} while ({PrintExpression(condition)});")
        {
            EmbeddedStatements = body,
            EmbeddedExpressions = [condition],
        };
    }

    private static TsStatement MapTry(IrTryStatement ts, IrToTsBridgeContext context)
    {
        var body = MapBody(ts.Body, context);

        var catches = (ts.Catches ?? [])
            .Select(c => (Variable: c.VariableName ?? "_err", Body: MapBody(c.Body, context)))
            .ToList();

        var finallyBody = ts.Finally is null ? null : MapBody(ts.Finally, context);
        var catchText = catches.Select(c =>
            $" catch ({c.Variable}) {{\n{RenderIndentedBody(c.Body)}}}"
        );
        var finallyText = finallyBody is null
            ? ""
            : $" finally {{\n{RenderIndentedBody(finallyBody)}}}";
        var text = $"try {{\n{RenderIndentedBody(body)}}}{string.Concat(catchText)}{finallyText}";

        return new TsRawStatement(text)
        {
            EmbeddedStatements =
            [
                .. body,
                .. catches.SelectMany(c => c.Body),
                .. finallyBody ?? [],
            ],
        };
    }

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

    internal static TsStatement Map(IrStatement statement, IrToTsBridgeContext context) =>
        statement switch
        {
            // Yield return surfaces as IrExpressionStatement(IrYieldExpression) —
            // peel it out and emit the dedicated TsYieldStatement.
            IrExpressionStatement { Expression: IrYieldExpression ye } => new TsYieldStatement(
                ye.Value is not null
                    ? IrToTsExpressionBridge.Map(ye.Value, context)
                    : new TsIdentifier("undefined")
            ),
            IrExpressionStatement { Expression: IrNullConditionalAccess access } =>
                IrToTsExpressionBridge.MapNullConditionalStatement(access, context),
            IrExpressionStatement es => new TsExpressionStatement(
                IrToTsExpressionBridge.MapNullTolerant(es.Expression, context)
            ),
            IrReturnStatement ret => new TsReturnStatement(
                ret.Value is not null ? IrToTsExpressionBridge.Map(ret.Value, context) : null
            ),
            // Declared ahead of its first assignment (an `out var` local, or
            // `T x;`): a typed, uninitialized `let x: T;` keeps TypeScript's
            // definite-assignment analysis in charge instead of `undefined`.
            IrVariableDeclaration { Initializer: null, Type: { } type } vd =>
                new TsVariableDeclaration(
                    vd.Name,
                    Initializer: null,
                    Const: false,
                    Type: IrToTsTypeMapper.Map(type),
                    DefinitelyAssigned: vd.AssignedInWhenGuard
                ),
            // Declared ahead of its first assignment with no known type (a `var`
            // local a switch section shares): a bare `let x;` takes the type of
            // its assignments.
            IrVariableDeclaration { Initializer: null, Type: null } vd => new TsVariableDeclaration(
                vd.Name,
                Initializer: null,
                Const: false
            ),
            // A bare `null` initializer narrows the local to `null` even under an
            // explicit annotation, so later member access resolves on `never`.
            // Casting the literal to the declared type keeps the full union.
            IrVariableDeclaration
            {
                Initializer: IrLiteral { Kind: IrLiteralKind.Null },
                Type: IrNullableTypeRef declaredType,
            } vd => new TsVariableDeclaration(
                vd.Name,
                new TsCastExpression(new TsLiteral("null"), IrToTsTypeMapper.Map(declaredType)),
                Const: vd.IsConst
            ),
            IrVariableDeclaration vd => new TsVariableDeclaration(
                vd.Name,
                vd.Initializer is not null
                    ? IrToTsExpressionBridge.Map(vd.Initializer, context)
                    : new TsIdentifier("undefined"),
                Const: vd.IsConst
            ),
            IrTupleDeconstruction td => new TsDestructuringDeclaration(
                td.Elements.Select(e => e.Name).ToList(),
                td.Initializer is IrOutArgumentCall outCall
                    ? IrToTsExpressionBridge.MapOutArgumentCallValue(outCall, context)
                    : IrToTsExpressionBridge.Map(td.Initializer, context),
                Const: td.IsConst
            ),
            IrIfStatement ifs => new TsIfStatement(
                IrToTsExpressionBridge.Map(ifs.Condition, context),
                MapBody(ifs.Then, context),
                ifs.Else is not null ? MapBody(ifs.Else, context) : null
            ),
            IrThrowStatement th => new TsThrowStatement(
                IrToTsExpressionBridge.Map(th.Expression, context)
            ),
            IrBlockStatement block => new TsBlockStatement(MapBody(block.Statements, context)),
            IrBreakStatement => new TsExpressionStatement(new TsIdentifier("break")),
            IrContinueStatement => new TsExpressionStatement(new TsIdentifier("continue")),
            IrSwitchStatement sw => IrToTsSwitchStatementBridge.Map(sw, context),
            IrYieldBreakStatement => new TsYieldBreakStatement(),
            IrForEachStatement fe => MapForEach(fe, context),
            IrForStatement fs => MapFor(fs, context),
            IrWhileStatement ws => MapWhile(ws, context),
            IrDoWhileStatement dw => MapDoWhile(dw, context),
            IrTryStatement ts => MapTry(ts, context),
            IrUnsupportedStatement u => new TsExpressionStatement(
                new TsIdentifier($"/* TODO: unsupported IR statement {u.Kind} */")
            ),
            _ => new TsExpressionStatement(
                new TsIdentifier($"/* TODO: {statement.GetType().Name} */")
            ),
        };
}
