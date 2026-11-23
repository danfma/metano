using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

internal static class IrToTsTypeParameterMapper
{
    public static IReadOnlyList<TsTypeParameter>? Convert(
        IReadOnlyList<IrTypeParameter>? typeParameters
    )
    {
        if (typeParameters is null || typeParameters.Count == 0)
            return null;

        return typeParameters.Select(tp => new TsTypeParameter(tp.Name, ConstraintOf(tp))).ToList();
    }

    /// <summary>
    /// The <c>extends</c> bound of a type parameter: its one constraint, or the
    /// intersection of several (<c>where T : A, B</c> lowers to
    /// <c>T extends A &amp; B</c>, #153). A constraint with no TypeScript
    /// declaration is dropped, as the heritage clauses drop it
    /// (<see cref="IrToTsClassBridge.ConvertHeritageList"/>): naming it would
    /// leave an unresolved type (<c>where T : IComparable&lt;T&gt;</c>, #547).
    /// </summary>
    private static TsType? ConstraintOf(IrTypeParameter typeParameter)
    {
        var bounds = (typeParameter.Constraints ?? [])
            .Select(constraint => (Ir: constraint, Ts: IrToTsTypeMapper.Map(constraint)))
            .Where(bound => HasDeclaration(bound.Ir, bound.Ts))
            .Select(bound => bound.Ts)
            .ToList();

        return bounds switch
        {
            [] => null,
            [var single] => single,
            _ => new TsIntersectionType(bounds),
        };
    }

    /// <summary>
    /// Whether the output can name the constraint's type: every constraint but
    /// a non-transpilable named type whose mapping binds no import (a BCL
    /// interface such as <c>IComparable&lt;T&gt;</c>, an <c>[Ignore]</c>d
    /// type). A BCL exception or comparer maps to its runtime class and stays.
    /// </summary>
    private static bool HasDeclaration(IrTypeRef constraint, TsType mapped) =>
        constraint is not IrNamedTypeRef { Semantics.IsTranspilable: false }
        || mapped is not TsNamedType { Origin: null, RuntimeHelperBinding: null }
        || BclExceptionClasses.IsBclException((IrNamedTypeRef)constraint);
}
