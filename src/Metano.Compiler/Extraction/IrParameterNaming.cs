using System.Runtime.CompilerServices;
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
/// </summary>
internal static class IrParameterNaming
{
    private const string CollisionSuffix = "$";

    private static readonly ConditionalWeakTable<ISymbol, HashSet<string>> CollidingNamesByOwner =
        new();

    public static string IrNameOf(IParameterSymbol parameter, Compilation? compilation) =>
        compilation is not null
        && IsRenamableOwner(parameter.ContainingSymbol)
        && CollidingNamesOf(parameter.ContainingSymbol, compilation).Contains(parameter.Name)
            ? parameter.Name + CollisionSuffix
            : parameter.Name;

    /// <summary>
    /// Only the parameters of an ordinary method, a module function or a
    /// lambda are renamed: those declare their parameters under the IR name
    /// and bind them as bare identifiers. A constructor parameter may be
    /// promoted to a member or captured, an accessor's <c>value</c> and an
    /// operator's parameters are declared elsewhere, and an <c>[ObjectArgs]</c>
    /// method's parameters are the keys of its public argument object, so they
    /// keep their name.
    /// </summary>
    private static bool IsRenamableOwner(ISymbol owner) =>
        owner
            is IMethodSymbol
            {
                MethodKind: MethodKind.Ordinary or MethodKind.AnonymousFunction
            } method
        && !method.IsExtensionBlockMember()
        && !method.HasObjectArgs();

    private static HashSet<string> CollidingNamesOf(ISymbol owner, Compilation compilation) =>
        CollidingNamesByOwner.GetValue(owner, key => FindCollidingNames(key, compilation));

    /// <summary>
    /// The names of the owner's parameters that share their emitted name with
    /// a containerless static method the owner's body references.
    /// </summary>
    private static HashSet<string> FindCollidingNames(ISymbol owner, Compilation compilation)
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

        foreach (var name in declaration.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if (
                model.GetSymbolInfo(name).Symbol is not IMethodSymbol callee
                || !IsFlattened(callee)
            )
                continue;

            var calleeName = callee.GetNameOverride() ?? SymbolHelper.ToCamelCase(callee.Name);

            colliding.UnionWith(
                parametersByEmittedName[calleeName].Select(parameter => parameter.Name)
            );
        }

        return colliding;
    }

    private static bool IsFlattened(IMethodSymbol callee) =>
        callee is { IsStatic: true } && callee.ContainingType.IsContainerless();
}
