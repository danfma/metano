using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.Transformation;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

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

        // Overload signatures are declaration-only — they do not introduce
        // parameter properties. Set Accessibility=None so the printer emits
        // a plain `n: number` instead of the (invalid-on-overload-sig)
        // `public n: number` form. Same applies to the rest-args impl
        // parameter further down. (#25.2)
        var overloads = sorted
            .Select(c => new TsConstructorOverload(
                c.Parameters.Select(p => new TsConstructorParam(
                        TypeScriptNaming.ToCamelCase(p.Parameter.Name),
                        IrToTsTypeMapper.Map(p.Parameter.Type),
                        Accessibility: TsAccessibility.None,
                        Rest: p.Parameter.IsParams
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
            [
                new TsConstructorParam(
                    "args",
                    new TsNamedType("unknown[]"),
                    Accessibility: TsAccessibility.None,
                    Rest: true
                ),
            ],
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

        // Bring the original parameter names back into scope as
        // `const <name> = args[i] as <Type>`. The dispatcher signature is
        // `(...args)`, so the lowered super(...) and body — which still
        // reference the source parameter names — would otherwise target
        // undefined identifiers. A single up-front rebind per branch keeps
        // the downstream lowering untouched and matches the legacy output
        // semantically.
        for (var i = 0; i < ctor.Parameters.Count; i++)
        {
            var param = ctor.Parameters[i].Parameter;
            branch.Add(
                new TsVariableDeclaration(
                    TypeScriptNaming.ToCamelCase(param.Name),
                    new TsCastExpression(
                        new TsElementAccess(new TsIdentifier("args"), new TsLiteral(i.ToString())),
                        IrToTsTypeMapper.Map(param.Type)
                    )
                )
            );
        }

        // When the constructor has a `: base(...)` initializer, emit the
        // matching super(...) call right after the parameter rebinds so the
        // arguments resolve to the just-declared locals.
        if (ctor.BaseArguments is { Count: > 0 } baseArgs)
        {
            var superArgs = baseArgs
                .Select(a => IrToTsExpressionBridge.MapArgument(a, bclRegistry))
                .ToList();
            branch.Add(
                new TsExpressionStatement(
                    new TsCallExpression(new TsIdentifier("super"), superArgs)
                )
            );
        }

        // Inline the constructor body. The body references the original
        // parameter names which are now in scope as the locals declared
        // above.
        if (ctor.Body is { Count: > 0 } body)
        {
            foreach (var stmt in body)
                branch.Add(IrToTsStatementBridge.Map(stmt, bclRegistry));
        }

        // Return so subsequent branches don't re-execute on the same call.
        branch.Add(new TsReturnStatement());
        return new TsIfStatement(condition, branch);
    }
}
