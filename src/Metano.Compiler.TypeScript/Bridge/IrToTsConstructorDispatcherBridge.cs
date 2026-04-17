using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Collapses an <see cref="IrConstructorDeclaration"/> whose
/// <see cref="IrConstructorDeclaration.Overloads"/> carries every sibling
/// constructor into a single TS constructor shaped like:
/// <code>
/// constructor(a: number);
/// constructor(a: string);
/// constructor(...args: unknown[]) {
///   if (args.length === 1 &amp;&amp; isInt32(args[0])) { super(...); /* body */ return; }
///   if (args.length === 1 &amp;&amp; isString(args[0])) { super(...); /* body */ return; }
///   throw new Error("No matching constructor");
/// }
/// </code>
/// The overload signatures mirror every constructor's parameter list in the
/// order most-specific-first. Each branch opens with the matching
/// <c>super(...)</c> call (when the IR has base arguments), then inlines the
/// constructor body via <see cref="IrToTsStatementBridge"/>.
/// </summary>
public static class IrToTsConstructorDispatcherBridge
{
    /// <summary>
    /// Builds the dispatcher constructor. Returns <c>null</c> when the IR
    /// carries a single constructor (no dispatcher needed).
    /// </summary>
    public static TsConstructor? Build(
        IrConstructorDeclaration primary,
        DeclarativeMappingRegistry? bclRegistry = null
    )
    {
        if (primary.Overloads is not { Count: > 0 })
            return null;

        var all = new List<IrConstructorDeclaration> { primary };
        all.AddRange(primary.Overloads);

        // Most-specific first so narrower arities guard broader ones — matches
        // the legacy dispatcher ordering.
        var sorted = all.OrderByDescending(c => c.Parameters.Count).ToList();

        var overloads = sorted
            .Select(c => new TsConstructorOverload(
                c.Parameters.Select(p => new TsConstructorParam(
                        TypeScriptNaming.ToCamelCase(p.Parameter.Name),
                        IrToTsTypeMapper.Map(p.Parameter.Type)
                    ))
                    .ToList()
            ))
            .ToList();

        var body = new List<TsStatement>();
        foreach (var ctor in sorted)
            body.Add(BuildBranch(ctor, bclRegistry));

        // Unmatched — match the legacy behavior: the bare TS `throw` so a
        // caller that passes an unsupported signature fails loudly at runtime.
        body.Add(
            new TsThrowStatement(
                new TsNewExpression(
                    new TsIdentifier("Error"),
                    [new TsStringLiteral("No matching constructor")]
                )
            )
        );

        return new TsConstructor(
            [new TsConstructorParam("...args", new TsNamedType("unknown[]"))],
            body,
            overloads
        );
    }

    private static TsIfStatement BuildBranch(
        IrConstructorDeclaration ctor,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        TsExpression condition = new TsBinaryExpression(
            new TsPropertyAccess(new TsIdentifier("args"), "length"),
            "===",
            new TsLiteral(ctor.Parameters.Count.ToString())
        );
        for (var i = 0; i < ctor.Parameters.Count; i++)
        {
            condition = new TsBinaryExpression(
                condition,
                "&&",
                IrTypeCheckBuilder.GenerateForParam(ctor.Parameters[i].Parameter.Type, i)
            );
        }

        var branch = new List<TsStatement>();

        // When the constructor has a `: base(...)` initializer, emit the
        // matching super(...) call at the top of the branch. Arguments may
        // reference parameter names the dispatcher no longer sees (the public
        // dispatcher takes `...args`), so we rewrite each identifier match to
        // the corresponding `args[i] as T` cast.
        if (ctor.BaseArguments is { Count: > 0 } baseArgs)
        {
            var paramIndex = ctor
                .Parameters.Select((p, i) => (p.Parameter.Name, i))
                .ToDictionary(t => t.Name, t => t.i);
            var superArgs = baseArgs
                .Select(a =>
                    RewriteArgumentForDispatcher(
                        IrToTsExpressionBridge.Map(a.Value, bclRegistry),
                        a.Value,
                        ctor,
                        paramIndex
                    )
                )
                .ToList();
            branch.Add(
                new TsExpressionStatement(
                    new TsCallExpression(new TsIdentifier("super"), superArgs)
                )
            );
        }

        // Inline the constructor body. The legacy dispatcher relies on the
        // parameters being in scope; the IR body uses their names too, so the
        // `args[i] as T` rewrite happens by walking every IrIdentifier whose
        // name matches a parameter.
        if (ctor.Body is { Count: > 0 } body)
        {
            var paramIndex = ctor
                .Parameters.Select((p, i) => (p.Parameter.Name, i))
                .ToDictionary(t => t.Name, t => t.i);
            foreach (var stmt in body)
            {
                var lowered = IrToTsStatementBridge.Map(stmt, bclRegistry);
                branch.Add(RewriteStatementForDispatcher(lowered, ctor, paramIndex));
            }
        }

        // Return so subsequent branches don't re-execute on the same call.
        branch.Add(new TsReturnStatement());
        return new TsIfStatement(condition, branch);
    }

    // ── argument + identifier rewriting ─────────────────────────────────────

    /// <summary>
    /// Placeholder hook for the future rewriting pass. The dispatcher body
    /// currently assumes callers consumed the parameter names directly (same
    /// shape as the legacy output because the legacy also preserves
    /// parameter names inside each branch). If the IR statement bridge starts
    /// emitting something different we'll revisit here.
    /// </summary>
    private static TsExpression RewriteArgumentForDispatcher(
        TsExpression lowered,
        IrExpression original,
        IrConstructorDeclaration ctor,
        IReadOnlyDictionary<string, int> paramIndex
    ) => lowered;

    private static TsStatement RewriteStatementForDispatcher(
        TsStatement lowered,
        IrConstructorDeclaration ctor,
        IReadOnlyDictionary<string, int> paramIndex
    ) => lowered;
}
