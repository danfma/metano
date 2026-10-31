using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

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
    /// <param name="primary">The primary constructor carrying the overloads.</param>
    /// <param name="bclRegistry">The declarative BCL mappings.</param>
    /// <param name="keepsBaseMessage">Whether the class overrides
    /// <c>Message</c>, so every <c>super(...)</c> is followed by the
    /// base-message capture.</param>
    public static TsConstructor? Build(
        IrConstructorDeclaration primary,
        DeclarativeMappingRegistry? bclRegistry = null,
        bool keepsBaseMessage = false
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
            body.Add(BuildBranch(ctor, keepsBaseMessage, bclRegistry));

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
        bool keepsBaseMessage,
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

        branch.AddRange(
            LowerConstructorInvocation(ctor, chainDepth: 0, keepsBaseMessage, bclRegistry)
        );

        // Return so subsequent branches don't re-execute on the same call.
        branch.Add(new TsReturnStatement());
        return new TsIfStatement(condition, branch);
    }

    /// <summary>
    /// The statements that run <paramref name="ctor"/> once its parameters are
    /// in scope: either its <c>: this(...)</c> delegation (see
    /// <see cref="LowerThisChain"/>) or its base call — the matching
    /// <c>super(...)</c> after any argument temporaries — followed by its
    /// body. Every path therefore reaches exactly one <c>super(...)</c> when
    /// the chain ends in a base call.
    /// </summary>
    private static IReadOnlyList<TsStatement> LowerConstructorInvocation(
        IrConstructorDeclaration ctor,
        int chainDepth,
        bool keepsBaseMessage,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var statements = new List<TsStatement>();

        if (ctor.ThisChain is { } chain)
            statements.AddRange(LowerThisChain(chain, chainDepth, keepsBaseMessage, bclRegistry));
        else if (
            IrToTsClassBridge.BuildBaseCall(ctor, keepsBaseMessage, bclRegistry) is { } baseCall
        )
            statements.AddRange(baseCall.ToStatements());

        // The body references the original parameter names, which the caller
        // already brought into scope.
        foreach (var statement in ctor.Body ?? [])
            statements.Add(IrToTsStatementBridge.Map(statement, bclRegistry));

        return statements;
    }

    /// <summary>
    /// Inlines a <c>: this(...)</c> delegation, since a TS constructor cannot
    /// call a sibling overload. The chained arguments are evaluated once, in
    /// source order, into temporaries; a nested block then binds the target's
    /// parameters to them (named arguments by name, missing ones to their
    /// default) and runs the target — recursively, so multi-level chains end
    /// in the single base call. The block keeps the target's parameter names
    /// from clashing with the caller's.
    /// </summary>
    private static IReadOnlyList<TsStatement> LowerThisChain(
        IrConstructorChain chain,
        int chainDepth,
        bool keepsBaseMessage,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var temporaries = chain
            .Arguments.Select(
                (argument, index) =>
                    new TsVariableDeclaration(
                        $"__chain{chainDepth}Arg{index}",
                        IrToTsExpressionBridge.Map(argument.Value, bclRegistry)
                    )
            )
            .ToList();

        var targetScope = new List<TsStatement>();
        var target = chain.Target;

        for (var index = 0; index < target.Parameters.Count; index++)
        {
            var parameter = target.Parameters[index].Parameter;

            targetScope.Add(
                new TsVariableDeclaration(
                    TypeScriptNaming.ToCamelCase(parameter.Name),
                    BindChainedParameter(chain, temporaries, parameter, index, bclRegistry)
                )
            );
        }

        targetScope.AddRange(
            LowerConstructorInvocation(target, chainDepth + 1, keepsBaseMessage, bclRegistry)
        );

        return [.. temporaries, new TsBlockStatement(targetScope)];
    }

    private static TsExpression BindChainedParameter(
        IrConstructorChain chain,
        IReadOnlyList<TsVariableDeclaration> temporaries,
        IrParameter parameter,
        int parameterIndex,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var argumentIndex = ChainedArgumentIndex(chain.Arguments, parameter.Name, parameterIndex);

        if (argumentIndex >= 0)
            return new TsIdentifier(temporaries[argumentIndex].Name);

        return parameter.DefaultValue is { } defaultValue
            ? IrToTsExpressionBridge.Map(defaultValue, bclRegistry)
            : new TsIdentifier("undefined");
    }

    /// <summary>
    /// The index of the chained argument bound to the parameter: a named
    /// argument by name, otherwise the positional argument at
    /// <paramref name="parameterIndex"/>; <c>-1</c> when the parameter is
    /// omitted.
    /// </summary>
    private static int ChainedArgumentIndex(
        IReadOnlyList<IrArgument> arguments,
        string parameterName,
        int parameterIndex
    )
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index].Name == parameterName)
                return index;
        }

        var isPositional =
            parameterIndex < arguments.Count && arguments[parameterIndex].Name is null;

        return isPositional ? parameterIndex : -1;
    }
}
