using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Tests;

/// <summary>
/// A cast that is the left operand of an operator the TypeScript parser would read as part
/// of its type (<c>x as E &lt;= y</c> reads <c>E&lt;</c> and fails at <c>=</c>) is
/// parenthesized; an operator that ends the type keeps the cast bare.
/// </summary>
public class TsCastContinuationTests
{
    [Test]
    [Arguments("<=", "(a as number) <= b")]
    [Arguments("<", "(a as number) < b")]
    [Arguments("|=", "(a as number) |= b")]
    [Arguments("&=", "(a as number) &= b")]
    [Arguments("<<=", "(a as number) <<= b")]
    [Arguments(">", "a as number > b")]
    [Arguments(">=", "a as number >= b")]
    [Arguments("||", "a as number || b")]
    [Arguments("&&", "a as number && b")]
    public async Task CastLeftOperand_IsParenthesizedOnlyWhereTheTypeWouldContinue(
        string op,
        string expected
    )
    {
        var cast = new TsCastExpression(new TsIdentifier("a"), new TsNumberType());
        var expression = new TsBinaryExpression(cast, op, new TsIdentifier("b"));

        await Assert.That(Printer.RenderExpression(expression)).IsEqualTo(expected);
    }
}
