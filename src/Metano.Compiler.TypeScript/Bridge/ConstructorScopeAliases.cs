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
/// (W10-1b). The instance initializers a constructor stores run in that scope
/// (<see cref="InstanceFieldStoreLowering"/>), where a parameter or local named
/// like a function or type they reference would shadow it;
/// renaming the parameter is not an option, as its name is API
/// (<c>[ObjectArgs]</c> keys, other targets).
/// </summary>
internal static class ConstructorScopeAliases
{
    private const string DispatcherArguments = "args";

    /// <summary>
    /// The aliases of the constructor scope being lowered, so every
    /// value-position type name (<see cref="IrToTsExpressionBridge.TypeValueReference"/>:
    /// a static receiver, a <c>new</c> callee, an <c>instanceof</c> operand, an
    /// enum member receiver) that a parameter or local shadows reads under its
    /// alias too, like the imported functions the bridge context aliases.
    /// <see cref="AsyncLocal{T}"/> keeps the slot per execution context, like
    /// the other per-file scopes of the transform.
    /// </summary>
    private static readonly AsyncLocal<ImmutableDictionary<string, string>?> _active = new();

    /// <summary>
    /// Runs <paramref name="lower"/> with <paramref name="aliases"/> as the
    /// active constructor scope (<see cref="AliasedTypeValue"/>).
    /// </summary>
    public static T InScope<T>(ImmutableDictionary<string, string> aliases, Func<T> lower)
    {
        var previous = _active.Value;

        _active.Value = aliases;

        try
        {
            return lower();
        }
        finally
        {
            _active.Value = previous;
        }
    }

    /// <inheritdoc cref="InScope{T}"/>
    public static void InScope(ImmutableDictionary<string, string> aliases, Action lower) =>
        InScope(aliases, () =>
        {
            lower();

            return true;
        });

    /// <summary>
    /// <paramref name="reference"/>, a value-position reference to the type
    /// emitted as <paramref name="name"/>, under its alias when the active
    /// constructor scope binds that name.
    /// </summary>
    public static TsExpression AliasedTypeValue(TsExpression reference, string name) =>
        _active.Value is { } aliases && aliases.TryGetValue(name, out var alias)
            ? new TsAliasedReference(reference, name, alias)
            : reference;

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
