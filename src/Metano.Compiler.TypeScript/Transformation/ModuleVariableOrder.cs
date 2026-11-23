using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The order a containerless class's module variables are declared in (#400): the C#
/// <c>const</c> fields first, then the rest, each group in declaration order. C# gives
/// a <c>const</c> no evaluation order and lets any initializer read it, while an ES
/// module evaluates its declarations eagerly in order, so a <c>static readonly</c>
/// initializer reading a <c>const</c> declared below it would hit the temporal dead
/// zone. The emission and the forward-read check both read this one order.
/// </summary>
internal static class ModuleVariableOrder
{
    public static IReadOnlyList<IrMemberDeclaration> Of(
        IReadOnlyList<IrMemberDeclaration> variables
    ) => variables.OrderBy(variable => IsConstant(variable) ? 0 : 1).ToList();

    public static IReadOnlyList<ISymbol> Of(IEnumerable<ISymbol> members) =>
        members.OrderBy(member => member is IFieldSymbol { IsConst: true } ? 0 : 1).ToList();

    private static bool IsConstant(IrMemberDeclaration variable) =>
        variable is IrFieldDeclaration { IsCompileTimeConstant: true };
}
