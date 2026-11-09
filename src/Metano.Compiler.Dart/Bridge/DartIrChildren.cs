using Metano.Compiler.Analysis;
using Metano.Compiler.Dart.AST;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart.Bridge;

/// <summary>
/// The child expressions of an expression after the Dart rewrite: the core's
/// <see cref="IrNodeChildren"/>, plus the arguments of the Dart-only
/// <see cref="DartNamedConstruction"/>, which the core walk does not know.
/// Every walker over rewritten Dart bodies reads children through it.
/// </summary>
internal static class DartIrChildren
{
    public static IEnumerable<IrExpression> ExpressionsOf(IrExpression expression) =>
        expression is DartNamedConstruction construction
            ? construction.Arguments.Select(argument => argument.Value)
            : IrNodeChildren.ExpressionsOf(expression);
}
