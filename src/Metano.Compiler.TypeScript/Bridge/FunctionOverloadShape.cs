using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// What a function dispatcher guards one overload by: its parameters and the
/// scope its type checks resolve against. Module functions, extension
/// methods and the static methods of a branded companion namespace dispatch
/// with no class scope, so the scope is the overload's own type parameters.
/// The two <c>MS0063</c> reporters read their dispatchers through this shape
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
}
