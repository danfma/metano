using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// What a function dispatcher guards one overload by: its parameters and the
/// scope its type checks resolve against. Module functions, extension
/// methods and the static methods of a branded companion namespace dispatch
/// with no class scope, so the scope is the overload's own type parameters.
/// The two <c>MS0063</c> detectors read their dispatchers through this shape
/// (issue #569).
/// </summary>
internal sealed record FunctionOverloadShape(
    IReadOnlyList<IrParameter> Parameters,
    TypeCheckScope Scope
)
{
    public static FunctionOverloadShape Of(IrMethodDeclaration method) =>
        new(method.Parameters, TypeCheckScope.ForMethod(method, [], typeOverrides: null));

    public static FunctionOverloadShape Of(IrModuleFunction function) =>
        new(function.Parameters, new TypeCheckScope(function.TypeParameters ?? [], null));

    /// <summary>
    /// Whether a dispatcher over <paramref name="overloads"/> can run another
    /// overload than the one C# binds: either <c>MS0063</c> detector
    /// (<see cref="BrandedSixtyFourBitOverloads"/>,
    /// <see cref="CatchAllOverloads"/>) finds something in the group.
    /// </summary>
    public static bool AreAmbiguous(IReadOnlyList<FunctionOverloadShape> overloads) =>
        BrandedSixtyFourBitOverloads.Find(overloads, overload => overload.Parameters).Count > 0
        || CatchAllOverloads
            .Find(overloads, overload => overload.Parameters, overload => overload.Scope)
            .Count > 0;
}
