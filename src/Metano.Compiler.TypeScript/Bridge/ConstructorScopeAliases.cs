using System.Collections.Immutable;
using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The names a class's constructor scope binds — its parameters (a
/// dispatcher's <c>args</c> and per-branch bindings too) and every local its
/// bodies declare, out-variable designations included — each with the alias
/// an imported binding of that name is read under inside the constructor
/// (W10-1b). The instance initializers a
/// constructor stores run in that scope (<see cref="InstanceFieldStoreLowering"/>),
/// where a parameter or local named like a function they call would shadow it;
/// renaming the parameter is not an option, as its name is API
/// (<c>[ObjectArgs]</c> keys, other targets).
/// </summary>
internal static class ConstructorScopeAliases
{
    private const string DispatcherArguments = "args";

    public static ImmutableDictionary<string, string> Of(
        IrClassDeclaration ir,
        IReadOnlyList<TsConstructorParam> parameters
    )
    {
        var bound = BoundNames(ir, parameters);

        return bound.ToImmutableDictionary(name => name, name => AliasOf(name, bound));
    }

    private static HashSet<string> BoundNames(
        IrClassDeclaration ir,
        IReadOnlyList<TsConstructorParam> parameters
    )
    {
        var bound = parameters
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (ir.Constructor is not { } constructor)
            return bound;

        bound.Add(DispatcherArguments);

        foreach (var declaration in constructor.DeclarationsInOrder)
        {
            bound.UnionWith(
                declaration.Parameters.Select(p =>
                    IrToTsNamingPolicy.ToParameterName(p.Parameter.Name)
                )
            );

            foreach (var statement in declaration.Body ?? [])
                AddLocals(statement, bound);
        }

        return bound;
    }

    private static void AddLocals(IrStatement statement, HashSet<string> bound)
    {
        bound.UnionWith(LocalsDeclaredBy(statement).Select(IrToTsNamingPolicy.ToLocalName));

        foreach (var expression in IrNodeChildren.ExpressionsOf(statement))
            AddOutLocals(expression, bound);

        foreach (var nested in IrNodeChildren.StatementsOf(statement))
            AddLocals(nested, bound);
    }

    private static IEnumerable<string> LocalsDeclaredBy(IrStatement statement) =>
        statement switch
        {
            IrVariableDeclaration local => [local.Name],
            IrForEachStatement loop => [loop.Variable],
            IrTupleDeconstruction deconstruction => deconstruction.BoundNames,
            _ => [],
        };

    /// <summary>
    /// Adds the <c>out var</c> / <c>out T x</c> locals an expression declares
    /// in the constructor scope; a lambda's body is its own scope, so the walk
    /// does not enter it.
    /// </summary>
    private static void AddOutLocals(IrExpression expression, HashSet<string> bound)
    {
        if (expression is IrOutArgumentCall call)
        {
            bound.UnionWith(
                call.OutArguments.Select(argument => argument.Target)
                    .OfType<IrOutLocalDeclaration>()
                    .Select(local => IrToTsNamingPolicy.ToLocalName(local.Name))
            );
        }

        foreach (var child in IrNodeChildren.ExpressionsOf(expression))
            AddOutLocals(child, bound);
    }

    private static string AliasOf(string name, HashSet<string> bound)
    {
        for (var attempt = 1; ; attempt++)
        {
            var candidate = $"{name}${attempt}";

            if (!bound.Contains(candidate))
                return candidate;
        }
    }
}
