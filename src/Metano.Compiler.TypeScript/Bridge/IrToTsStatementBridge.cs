using System.Globalization;
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
    /// The FR-058 guard text the generated code reads the production-checks
    /// flag with (contracts/runtime-collections.md § Production checks).
    /// </summary>
    internal const string ProductionChecksGuard =
        "typeof __METANO_CHECKS__ === \"undefined\" || __METANO_CHECKS__";

    /// <summary>
    /// Maps a list of IR statements to TS statements. A nested
    /// <see cref="IrBlockStatement"/> stays a <c>{ … }</c> block so the
    /// <c>let</c>/<c>const</c> bindings it declares keep their C# scope (a bare
    /// C# block, or a loop wrapped with its hoisted <c>out var</c> locals). The
    /// temporaries its expressions assign inline are declared at its head
    /// (<see cref="TsTemporaries"/>).
    /// </summary>
    internal static IReadOnlyList<TsStatement> MapBody(
        IReadOnlyList<IrStatement> body,
        IrToTsBridgeContext context
    ) => TsTemporaries.Around(context, scoped => body.Select(stmt => Map(stmt, scoped)).ToList());

    /// <summary>
    /// Maps the statements of a <c>switch</c> section. Every <c>case</c>
    /// clause of a JS <c>switch</c> shares one scope, so a section declares no
    /// temporaries of its own: they go to the head of the statement list that
    /// encloses the <c>switch</c> (two sections each declaring <c>let $s</c>
    /// would redeclare it, #735). Outside a statement list the section opens
    /// its own scope, as before.
    /// </summary>
    internal static IReadOnlyList<TsStatement> MapSectionBody(
        IReadOnlyList<IrStatement> body,
        IrToTsBridgeContext context
    ) =>
        context.Temporaries is null
            ? MapBody(body, context)
            : body.Select(statement => Map(statement, context)).ToList();

    private static TsStatement MapForEach(IrForEachStatement fe, IrToTsBridgeContext context)
    {
        if (!fe.IsAwait && fe.CollectionType is IrCollectionTypeRef { Kind: IrCollectionKind.List })
            return MapListForEach(fe, context);

        if (fe.CollectionType is IrPrimitiveTypeRef { Primitive: IrPrimitive.String })
            return MapStringForEach(fe, context);

        // C# `foreach (var x in xs) { … }` → TS `for (const x of xs) { … }`;
        // `await foreach` → `for await (…)` (#625).
        var collection = IrToTsExpressionBridge.Map(fe.Collection, context);
        var body = MapBody(fe.Body, context);
        var loopVariable = IrToTsNamingPolicy.ToLocalName(fe.Variable);
        var loop = $"{LabelOf(fe)}{(fe.IsAwait ? "for await" : "for")}";
        var header = $"{loop} (const {loopVariable} of {PrintExpression(collection)}) {{";

        return new TsRawStatement(WrapLoop(header, body))
        {
            EmbeddedExpressions = [collection],
            EmbeddedScopes = [new TsEmbeddedScope([loopVariable], body)],
        };
    }

    /// <summary>
    /// <c>foreach</c> over a <c>List&lt;T&gt;</c> (spec 011 FR-057,
    /// contracts/runtime-collections.md § Lowered <c>foreach</c>, final): the
    /// indexed loop over the backing array, with the receiver bound once and
    /// the version and length checked at the start of every step (the final
    /// one included, never after a <c>break</c> / <c>return</c>), each check
    /// under the function-local FR-058 flag <c>$c</c>, read first so a bundler
    /// folds it. Its locals and every temporary of the body come from one
    /// allocator (<see cref="TsTemporaries.Reserve"/>), so a nested loop or a
    /// comma-shape update in the body never shadows them (<c>$i1</c>, …). The
    /// element read is asserted (<c>$a[$i]!</c>): the index is in range, which
    /// <c>noUncheckedIndexedAccess</c> cannot see.
    /// </summary>
    private static TsStatement MapListForEach(IrForEachStatement fe, IrToTsBridgeContext context)
    {
        var names = TsTemporaries.Reserve(
            context,
            ["$c", "$l", "$a", "$n", "$v", "$i"],
            out var bodyContext
        );

        var (checks, list, items, length, version, index) = (
            names[0],
            names[1],
            names[2],
            names[3],
            names[4],
            names[5]
        );

        var collection = IrToTsExpressionBridge.Map(fe.Collection, context);
        var loopVariable = IrToTsNamingPolicy.ToLocalName(fe.Variable);

        var body = MapBody(fe.Body, bodyContext);

        var throwModified = new TsCallExpression(
            RuntimeCollections.HelperReference("throwModified"),
            []
        );

        var header = string.Join(
            "\n",
            "{",
            $"  const {checks} = {ProductionChecksGuard};",
            $"  const {list} = {PrintExpression(collection)};",
            $"  const {items} = {list}._items;",
            $"  const {length} = {items}.length;",
            $"  const {version} = {checks} ? {list}._version : 0;",
            $"  {LabelOf(fe)}for (let {index} = 0; ; {index}++) {{",
            $"    if ({checks} && ({list}._version !== {version} || {items}.length !== {length})) {PrintExpression(throwModified)};",
            $"    if ({index} === {length}) break;",
            $"    const {loopVariable} = {items}[{index}]!;"
        );

        return new TsRawStatement(BlockWrappedLoop(header, body))
        {
            EmbeddedExpressions = [collection, throwModified],
            EmbeddedScopes = [new TsEmbeddedScope([loopVariable], body)],
        };
    }

    /// <summary>
    /// <c>foreach</c> over a <c>string</c> (#695): C# walks its UTF-16 code
    /// units, where a JS string's own iterator yields code points, so the
    /// loop indexes the string, bound once as C# evaluates the collection
    /// once. No array is built and no iterator is allocated. The element
    /// read is asserted (<c>$s[$i]!</c>): the index is in range, which
    /// <c>noUncheckedIndexedAccess</c> cannot see.
    /// </summary>
    private static TsStatement MapStringForEach(IrForEachStatement fe, IrToTsBridgeContext context)
    {
        var names = TsTemporaries.Reserve(context, ["$s", "$i"], out var bodyContext);
        var (text, index) = (names[0], names[1]);

        var collection = IrToTsExpressionBridge.Map(fe.Collection, context);
        var loopVariable = IrToTsNamingPolicy.ToLocalName(fe.Variable);

        var body = MapBody(fe.Body, bodyContext);

        var header = string.Join(
            "\n",
            "{",
            $"  const {text} = {PrintExpression(collection)};",
            $"  {LabelOf(fe)}for (let {index} = 0; {index} < {text}.length; {index}++) {{",
            $"    const {loopVariable} = {text}[{index}]!;"
        );

        return new TsRawStatement(BlockWrappedLoop(header, body))
        {
            EmbeddedExpressions = [collection],
            EmbeddedScopes = [new TsEmbeddedScope([loopVariable], body)],
        };
    }

    /// <summary>
    /// The text of a lowered loop opened by <paramref name="header"/> inside
    /// its own block (<c>{ … for (…) {</c>): the body indented one more
    /// level, then the loop and the block closed.
    /// </summary>
    private static string BlockWrappedLoop(string header, IReadOnlyList<TsStatement> body)
    {
        var printedBody = RenderIndentedBody(body).TrimEnd('\n');

        var indentedBody = string.Join(
            "\n",
            printedBody.Split('\n').Select(line => line.Length == 0 ? line : "  " + line)
        );

        return (indentedBody.Length == 0 ? header : header + "\n" + indentedBody) + "\n  }\n}";
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
            $"{LabelOf(fs)}for ({RenderForInitializers(initializers)}; {conditionText}; {RenderCommaList(incrementors)}) {{";

        return new TsRawStatement(WrapLoop(header, body))
        {
            EmbeddedScopes =
            [
                new TsEmbeddedScope([], [.. initializers, .. body])
                {
                    Expressions =
                    [
                        .. condition is null ? [] : new[] { condition },
                        .. incrementors,
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// The TypeScript type of <paramref name="localType"/> when it is an
    /// array (<c>T[]</c>, <c>T[] | null</c>); <c>null</c> for any other.
    /// </summary>
    private static TsType? ArrayShapedTypeOf(IrTypeRef localType, IrToTsBridgeContext context)
    {
        var mapped = IrToTsTypeMapper.Map(localType, context.TypeOverrides);

        var isArrayShaped =
            mapped
            is TsArrayType
                or TsUnionType { Types: [TsArrayType, TsNamedType { Name: "null" }] };

        return isArrayShaped ? mapped : null;
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
    /// One declarator of a <c>let</c> list: <c>name = value</c>,
    /// <c>name: T = value</c> for a typed declaration, or <c>name: T</c> for
    /// one without an initializer.
    /// </summary>
    private static string RenderDeclarator(TsVariableDeclaration declaration) =>
        declaration switch
        {
            { Type: { } type, Initializer: null } =>
                $"{declaration.Name}{DefiniteAssignmentMark(declaration)}: {Printer.RenderType(type)}",
            { Type: { } type, Initializer: { } value } =>
                $"{declaration.Name}: {Printer.RenderType(type)} = {PrintExpression(value)}",
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

    /// <summary>
    /// The JavaScript label a loop's header starts with (<c>outer: </c>), the
    /// target of a labeled <c>break</c> / <c>continue</c> (#772); empty for a
    /// loop without one. A loop lowered inside its own block carries the
    /// label on the inner loop, which the jumps target.
    /// </summary>
    private static string LabelOf(IrBreakableStatement loop) =>
        loop.Label is { } label ? $"{TypeScriptNaming.ToLabel(label)}: " : string.Empty;

    /// <summary><c>break</c> / <c>continue</c>, naming the label it targets, if any.</summary>
    private static TsExpressionStatement Jump(string keyword, string? label) =>
        new(
            new TsIdentifier(
                label is null ? keyword : $"{keyword} {TypeScriptNaming.ToLabel(label)}"
            )
        );

    private static TsStatement MapWhile(IrWhileStatement ws, IrToTsBridgeContext context)
    {
        var condition = IrToTsExpressionBridge.Map(ws.Condition, context);
        var body = MapBody(ws.Body, context);

        var header = $"{LabelOf(ws)}while ({PrintExpression(condition)}) {{";

        return new TsRawStatement(WrapLoop(header, body))
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

        return new TsRawStatement(
            $"{LabelOf(dw)}do {{\n{inner}}} while ({PrintExpression(condition)});"
        )
        {
            EmbeddedStatements = body,
            EmbeddedExpressions = [condition],
        };
    }

    private static TsTryStatement MapTry(IrTryStatement ts, IrToTsBridgeContext context)
    {
        var handler = ts.Catches is { Count: > 0 } clauses
            ? IrToTsCatchChainBridge.Map(clauses, context)
            : null;

        var finallyBody = ts.Finally is null ? null : MapBody(ts.Finally, context);

        return new TsTryStatement(MapBody(ts.Body, context), handler, finallyBody);
    }

    /// <summary>
    /// The emitted name of a deconstruction element; a discard binds nothing.
    /// </summary>
    private static string? LocalElementName(IrDeconstructionElement element) =>
        element.Name is { } name ? IrToTsNamingPolicy.ToLocalName(name) : null;

    /// <summary>One slot of a deconstruction that stores into declared locals.</summary>
    private static TsExpression AssignedElement(IrDeconstructionElement element) =>
        LocalElementName(element) is { } name ? new TsIdentifier(name) : new TsArrayHole();

    private static TsExpression DeconstructedValue(
        IrTupleDeconstruction td,
        IrToTsBridgeContext context
    ) =>
        td.Initializer is IrOutArgumentCall outCall
            ? IrToTsExpressionBridge.MapOutArgumentCallValue(outCall, context)
            : IrToTsExpressionBridge.Map(td.Initializer, context);

    private static string WrapLoop(string header, IReadOnlyList<TsStatement> body) =>
        header + "\n" + RenderIndentedBody(body) + "}";

    internal static string RenderIndentedBody(IReadOnlyList<TsStatement> body)
    {
        var file = new TsSourceFile(
            "frag.ts",
            [new TsFunction("frag", [], new TsVoidType(), body)]
        );
        var printed = new Printer().PrintWithDeferredHelperNames(file);
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

    internal static string PrintExpression(TsExpression expr)
    {
        var stmt = new TsRawStatement("__PLACEHOLDER__");
        var file = new TsSourceFile(
            "frag.ts",
            [new TsFunction("frag", [], new TsVoidType(), [new TsExpressionStatement(expr)])]
        );
        var printed = new Printer().PrintWithDeferredHelperNames(file);
        // Extract the expression statement from inside the function body.
        var start = printed.IndexOf('{') + 1;
        var end = printed.LastIndexOf('}');
        var inner = printed[start..end].Trim();
        return inner.TrimEnd(';');
    }

    internal static TsStatement Map(IrStatement statement, IrToTsBridgeContext context)
    {
        using var location = DroppedCodeDiagnostics.At(statement);

        return MapAtLocation(statement, context);
    }

    private static TsStatement MapAtLocation(IrStatement statement, IrToTsBridgeContext context) =>
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
            IrExpressionStatement { Expression: IrBinaryExpression store }
                when IrToTsExpressionBridge.IsIndexerStore(store) => new TsExpressionStatement(
                IrToTsExpressionBridge.MapIndexerStore(store, context, isStatement: true)
            ),
            IrExpressionStatement { Expression: IrIndexerUpdateExpression update } =>
                new TsExpressionStatement(
                    IrToTsExpressionBridge.MapIndexerUpdate(update, context, isStatement: true)
                ),
            IrExpressionStatement es => new TsExpressionStatement(
                IrToTsExpressionBridge.MapNullTolerant(es.Expression, context)
            ),
            IrInlineCodeStatement inlineCode => IrToTsInlineCodeBridge.MapStatement(
                inlineCode,
                context
            ),
            IrReturnStatement { Value: IrSwitchExpression sw }
                when IrToTsExpressionBridge.TryMapSwitchReturn(sw, context) is { } switchReturn =>
                switchReturn,
            IrReturnStatement ret => new TsReturnStatement(
                ret.Value is not null ? IrToTsExpressionBridge.Map(ret.Value, context) : null
            ),
            // Declared ahead of its first assignment (an `out var` local, a
            // pattern designator, or `T x;`): a typed, uninitialized
            // `let x: T;` keeps TypeScript's definite-assignment analysis in
            // charge instead of `undefined`. The [ExportFromBcl] overrides
            // ride along so a decimal designator narrows to `Decimal` — the
            // type the `instanceof` test just established for its subject.
            IrVariableDeclaration { Initializer: null, Type: { } type } vd =>
                new TsVariableDeclaration(
                    IrToTsNamingPolicy.ToLocalName(vd.Name),
                    Initializer: null,
                    Const: false,
                    Type: IrToTsTypeMapper.Map(type, context.TypeOverrides),
                    DefinitelyAssigned: vd.AssignedInWhenGuard
                ),
            // Declared ahead of its first assignment with no known type (a `var`
            // local a switch section shares): a bare `let x;` takes the type of
            // its assignments.
            IrVariableDeclaration { Initializer: null, Type: null } vd => new TsVariableDeclaration(
                IrToTsNamingPolicy.ToLocalName(vd.Name),
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
                IrToTsNamingPolicy.ToLocalName(vd.Name),
                new TsCastExpression(new TsLiteral("null"), IrToTsTypeMapper.Map(declaredType)),
                Const: vd.IsConst
            ),
            // An array literal C# types wider than its elements say
            // (`List<int?> l = [1]`, `List<int> l = []`): a local declared
            // with an array type carries that type, which TypeScript would
            // otherwise infer narrower. Any other declared type
            // (`IReadOnlyCollection<T>` is an `Iterable<T>`) would hide the
            // array the local holds, so there the literal is asserted.
            IrVariableDeclaration
            {
                Type: { } localType,
                Initializer: IrArrayLiteral { ElementType: not null } literal,
            } vd when ArrayShapedTypeOf(localType, context) is { } arrayType =>
                new TsVariableDeclaration(
                    IrToTsNamingPolicy.ToLocalName(vd.Name),
                    IrToTsExpressionBridge.MapBareArrayLiteral(literal, context),
                    Const: vd.IsConst,
                    Type: arrayType
                ),
            // A `dynamic` local keeps its `any` annotation: TypeScript would
            // otherwise narrow it to its initializer (`dynamic d = 1` to
            // `number`) and reject the operations bound at run time on it.
            IrVariableDeclaration { Type: IrDynamicTypeRef, Initializer: { } initializer } vd =>
                new TsVariableDeclaration(
                    IrToTsNamingPolicy.ToLocalName(vd.Name),
                    IrToTsExpressionBridge.Map(initializer, context),
                    Const: vd.IsConst,
                    Type: new TsAnyType()
                ),
            // A reassigned local declared nullable (`Link? current = link;`):
            // TypeScript would type it by its initializer and reject a later
            // `null` store, so the declared type stays (#774). A `var` local
            // has no declared type and follows its inferred one.
            IrVariableDeclaration
            {
                Type: IrNullableTypeRef declaredType,
                Initializer: { } initializer,
                IsConst: false,
            } vd => new TsVariableDeclaration(
                IrToTsNamingPolicy.ToLocalName(vd.Name),
                IrToTsExpressionBridge.Map(initializer, context),
                Const: false,
                Type: IrToTsTypeMapper.Map(declaredType, context.TypeOverrides)
            ),
            IrVariableDeclaration { Initializer: { } initializer } vd
                when EnumValueAssertion.IsClosedNumericEnum(vd.Type ?? vd.InferredType) =>
                new TsVariableDeclaration(
                    IrToTsNamingPolicy.ToLocalName(vd.Name),
                    EnumValueAssertion.AsEnum(
                        IrToTsExpressionBridge.Map(initializer, context),
                        (vd.Type ?? vd.InferredType)!
                    ),
                    Const: vd.IsConst
                ),
            IrVariableDeclaration vd => new TsVariableDeclaration(
                IrToTsNamingPolicy.ToLocalName(vd.Name),
                vd.Initializer is not null
                    ? IrToTsExpressionBridge.Map(vd.Initializer, context)
                    : new TsIdentifier("undefined"),
                Const: vd.IsConst
            ),
            IrTupleDeconstruction { IsAssignment: true } td => new TsExpressionStatement(
                new TsBinaryExpression(
                    new TsArrayLiteral([.. td.Elements.Select(AssignedElement)]),
                    "=",
                    DeconstructedValue(td, context)
                )
            ),
            IrTupleDeconstruction td => IrToTsDeconstructionBridge.Map(td, context),
            IrIfStatement ifs => new TsIfStatement(
                IrToTsExpressionBridge.Map(ifs.Condition, context),
                MapBody(ifs.Then, context),
                ifs.Else is not null ? MapBody(ifs.Else, context) : null
            ),
            IrThrowStatement th => new TsThrowStatement(
                IrToTsExpressionBridge.Map(th.Expression, context)
            ),
            IrRethrowStatement => new TsThrowStatement(
                new TsIdentifier(
                    context.CaughtValue
                        ?? throw new InvalidOperationException(
                            "A 'throw;' reached the bridge outside a catch clause's body."
                        )
                )
            ),
            IrBlockStatement block => new TsBlockStatement(MapBody(block.Statements, context)),
            IrBreakStatement jump => Jump("break", jump.Label),
            IrContinueStatement jump => Jump("continue", jump.Label),
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
            _ => DroppedStatement(statement),
        };

    /// <summary>
    /// A node the bridge has no lowering for: its placeholder drops the
    /// statement, so it reports an error (W9-13). An
    /// <see cref="IrUnsupportedStatement"/> was already reported where it was
    /// extracted.
    /// </summary>
    private static TsStatement DroppedStatement(IrStatement statement)
    {
        var kind = statement.GetType().Name;

        DroppedCodeDiagnostics.Report(
            $"The C# {DroppedCodeDiagnostics.ConstructName(statement)} is not supported by "
                + "the TypeScript target."
        );

        return new TsExpressionStatement(new TsIdentifier($"/* TODO: {kind} */"));
    }
}
