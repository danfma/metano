using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Separates C#'s integer division from floating-point division. Both share
/// the <c>/</c> token, but over integral operands C# truncates the quotient
/// toward zero and throws on a zero divisor, so the extractor marks them as
/// <see cref="IrBinaryOp.IntegerDivide"/> for each backend to lower.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <see cref="IrBinaryOp.IntegerDivide"/> when <paramref name="op"/> is
    /// <see cref="IrBinaryOp.Divide"/> and <paramref name="operation"/> (a
    /// binary <c>/</c> or a compound <c>/=</c>) binds to an integer division;
    /// <paramref name="op"/> unchanged otherwise.
    /// </summary>
    private IrBinaryOp NarrowToIntegerDivision(IrBinaryOp op, ExpressionSyntax operation) =>
        op is IrBinaryOp.Divide && IsIntegerDivision(operation) ? IrBinaryOp.IntegerDivide : op;

    /// <summary>
    /// Whether Roslyn bound <paramref name="operation"/> to a built-in
    /// division whose result is integral. Byte, short and char operands are
    /// promoted to <c>int</c> first, so the result type covers them, and a
    /// lifted <c>int?</c> division reports its nullable result. User-defined
    /// operators (<c>BigInteger</c>, user structs) and the floating-point and
    /// decimal operators do not match.
    /// </summary>
    private bool IsIntegerDivision(ExpressionSyntax operation) =>
        _semantic.GetSymbolInfo(operation).Symbol
            is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator } divide
        && IsIntegralNumber(UnwrapNullable(divide.ReturnType));

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static bool IsIntegralNumber(ITypeSymbol type) =>
        type.SpecialType
            is SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr
                or SpecialType.System_Char;
}
