using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Metano.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The IR name of a parameter. A call to a member of a containerless static
/// class (<c>[NoContainer]</c>) lowers to the bare function name, so a
/// parameter of the same camelCase name shadows it: C#'s <c>Log(log)</c>
/// becomes <c>log(log)</c>, which calls the parameter. Such a parameter takes a
/// <c>$</c> suffix, both where it is declared and where it is read; any other
/// parameter keeps its name. A shadowed module variable is a different case,
/// rejected with MS0001 by the module-variable validator.
/// <para>
/// The callee's emitted name depends on the target: a <c>[Name]</c> may be
/// targeted, and on an overload of a dispatched group it pins a suffix rather
/// than renaming the member (<see cref="OverloadEmittedName"/>). The colliding
/// names are therefore computed, and cached, per owner and grouping policy.
/// </para>
/// </summary>
internal static class IrParameterNaming
{
    private const string CollisionSuffix = "$";

    private static readonly ConditionalWeakTable<
        ISymbol,
        ConcurrentDictionary<OverloadGroupingPolicy, HashSet<string>>
    > CollidingNamesByOwner = new();

    public static string IrNameOf(
        IParameterSymbol parameter,
        Compilation? compilation,
        OverloadGroupingPolicy policy
    ) =>
        compilation is not null
        && IsRenamableOwner(parameter.ContainingSymbol)
        && CollidingNamesOf(parameter.ContainingSymbol, compilation, policy)
            .Contains(parameter.Name)
            ? parameter.Name + CollisionSuffix
            : parameter.Name;

    /// <summary>
    /// Whether the local-name disambiguator (#398) may rename the parameter
    /// in a camelCase collision: a parameter of an owner that takes the
    /// shadow suffix (<see cref="IsRenamableOwner"/>), or an operator's — an
    /// operator is dispatched through the IR, never by a target caller naming
    /// a parameter, and its declaration reads the disambiguated name.
    /// </summary>
    public static bool IsRenamable(IParameterSymbol parameter) =>
        IsRenamableOwner(parameter.ContainingSymbol) || IsOperator(parameter.ContainingSymbol);

    /// <summary>
    /// The owners whose parameters take the shadow suffix: an ordinary
    /// method, a module function and a lambda, which declare their parameters
    /// under the IR name and bind them as bare identifiers. A constructor
    /// parameter may be promoted to a member or captured, an accessor's
    /// <c>value</c> is implicit, and an <c>[ObjectArgs]</c> method's
    /// parameters are the keys of its public argument object, so they keep
    /// their name. An operator's parameters never take the shadow suffix,
    /// though the local-name disambiguator may rename them
    /// (<see cref="IsRenamable"/>).
    /// </summary>
    private static bool IsRenamableOwner(ISymbol owner) =>
        owner
            is IMethodSymbol
            {
                MethodKind: MethodKind.Ordinary or MethodKind.AnonymousFunction
            } method
        && !method.IsExtensionBlockMember()
        && !method.HasObjectArgs();

    private static bool IsOperator(ISymbol owner) =>
        owner is IMethodSymbol method && method.IsUserDefinedOperatorOrConversion();

    private static HashSet<string> CollidingNamesOf(
        ISymbol owner,
        Compilation compilation,
        OverloadGroupingPolicy policy
    ) =>
        CollidingNamesByOwner
            .GetValue(owner, _ => new ConcurrentDictionary<
                OverloadGroupingPolicy,
                HashSet<string>
            >())
            .GetOrAdd(policy, _ => FindCollidingNames(owner, compilation, policy));

    /// <summary>
    /// The names of the owner's parameters that share their emitted name with
    /// a containerless static method the owner's body references.
    /// </summary>
    private static HashSet<string> FindCollidingNames(
        ISymbol owner,
        Compilation compilation,
        OverloadGroupingPolicy policy
    )
    {
        var colliding = new HashSet<string>();

        if (owner is not IMethodSymbol { Parameters.Length: > 0 } method)
            return colliding;

        if (owner.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not { } declaration)
            return colliding;

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var parametersByEmittedName = method.Parameters.ToLookup(parameter =>
            SymbolHelper.ToCamelCase(parameter.Name)
        );

        foreach (var calledName in FlattenedNamesCalledIn(declaration, model, policy))
        {
            colliding.UnionWith(
                parametersByEmittedName[calledName].Select(parameter => parameter.Name)
            );
        }

        return colliding;
    }

    /// <summary>
    /// Every bare name a flattened call under <paramref name="root"/> may spell (<see
    /// cref="CalledNamesOf"/>): the names a binding of that root must not take, since
    /// it would shadow the function it calls. The local-name disambiguator renames a
    /// local that takes one of them.
    /// </summary>
    internal static IReadOnlySet<string> FlattenedNamesCalledIn(
        SyntaxNode root,
        SemanticModel model,
        OverloadGroupingPolicy policy
    )
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if (
                model.GetSymbolInfo(name).Symbol is IMethodSymbol callee
                && IsFlattened(callee, policy)
            )
                names.UnionWith(CalledNamesOf(callee, policy));
        }

        return names;
    }

    /// <summary>
    /// The names a flattened call to <paramref name="callee"/> may spell: its own name
    /// and, for an overload of a dispatched group, the fast path a bound call emits
    /// instead (spec 013 slice 013e, #578) — the dispatcher's name plus the overload's
    /// suffix. A fast path the barrel claim pass renamed (MS0067) ends in <c>$</c>,
    /// which no C# parameter can spell.
    /// </summary>
    private static IEnumerable<string> CalledNamesOf(
        IMethodSymbol callee,
        OverloadGroupingPolicy policy
    )
    {
        var name = OverloadEmittedName.NameOverrideOf(callee, policy)
            ?? SymbolHelper.ToCamelCase(callee.Name);

        yield return name;

        var definition = callee.OriginalDefinition;

        if (
            MethodOverloadGroups.GroupOf(definition, policy) is { IsDispatched: true } group
            && group.MemberOf(definition) is { } member
        )
        {
            var suffixes = MethodOverloadGroups.SuffixesOf(
                definition.ContainingType,
                group,
                policy
            );

            yield return name + suffixes[member];
        }
    }

    /// <summary>
    /// Whether a call to <paramref name="callee"/> may lower to its bare function name:
    /// a static method of a containerless class, or of an emitted extension container,
    /// which a target may emit as a module too (#554).
    /// </summary>
    private static bool IsFlattened(IMethodSymbol callee, OverloadGroupingPolicy policy) =>
        callee is { IsStatic: true, ContainingType: { } owner }
        && (owner.IsContainerless() || owner.IsEmittedExtensionContainer(policy.Target));
}
