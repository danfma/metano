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
        && CollidingNamesOf(parameter.ContainingSymbol, compilation).Contains(parameter.Name)
            ? parameter.Name + CollisionSuffix
            : parameter.Name;

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

        foreach (var name in declaration.DescendantNodes().OfType<IdentifierNameSyntax>())
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
