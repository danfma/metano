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
/// <param name="IsOperator">The overload is a user-defined operator or
/// conversion, which only a branded companion declares as a function.</param>
internal sealed record FunctionOverloadShape(
    IReadOnlyList<IrParameter> Parameters,
    TypeCheckScope Scope,
    bool IsOperator
)
{
    public static FunctionOverloadShape Of(IrMethodDeclaration method) =>
        new(
            method.Parameters,
            TypeCheckScope.ForMethod(method, [], typeOverrides: null),
            method.Semantics.IsOperator
        );

    public static FunctionOverloadShape Of(IrModuleFunction function) =>
        new(
            function.Parameters,
            new TypeCheckScope(function.TypeParameters ?? [], null),
            IsOperator: false
        );
}
