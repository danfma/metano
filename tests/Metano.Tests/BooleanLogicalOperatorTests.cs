namespace Metano.Tests;

/// <summary>
/// C#'s <c>&amp;</c>, <c>|</c> and <c>^</c> over booleans (#323) evaluate both
/// operands, unlike <c>&amp;&amp;</c> / <c>||</c>, while JS applies them to
/// numbers and TS rejects them on booleans (TS2447). Each shape was checked
/// with <c>tsgo --strict</c> and run under Bun against the C# truth table.
/// </summary>
public class BooleanLogicalOperatorTests
{
    [Test]
    public async Task Bool_AndOrXor_EvaluateBothOperandsWithoutNumericOperators()
    {
        var output = Transpile(
            """
            public static bool And(bool a, bool b) => a & b;
            public static bool Or(bool a, bool b) => a | b;
            public static bool Xor(bool a, bool b) => a ^ b;
            """
        );

        await Assert.That(output).Contains("return !!(+a & +b);");
        await Assert.That(output).Contains("return !!(+a | +b);");
        await Assert.That(output).Contains("return a !== b;");
    }

    [Test]
    public async Task Bool_SideEffectingOperands_AreEachEvaluatedOnceInOrder()
    {
        var output = Transpile(
            """
            public static bool Touch(bool v) => v;
            public static bool Both() => Touch(false) & Touch(true);
            """
        );

        await Assert.That(output).Contains("return !!(+Logic.touch(false) & +Logic.touch(true));");
    }

    [Test]
    public async Task IntegerAndEnumOperands_KeepTheBitwiseOperators()
    {
        var output = Transpile(
            """
            public static int Mask(int a, int b) => a & b;
            public static int Merge(int a, int b) => a | b;
            public static Flag Both(Flag a, Flag b) => a & b;
            """
        );

        await Assert.That(output).Contains("return a & b;");
        await Assert.That(output).Contains("return a | b;");
    }

    [Test]
    public async Task NullableBool_And_FalseWinsOverNullWhichWinsOverTrue()
    {
        var output = Transpile("public static bool? Both(bool? a, bool? b) => a & b;");

        await Assert
            .That(output)
            .Contains(
                "return a === false || b === false ? false : a == null || b == null ? null : true;"
            );
    }

    [Test]
    public async Task NullableBool_Or_TrueWinsOverNullWhichWinsOverFalse()
    {
        var output = Transpile("public static bool? Either(bool? a, bool? b) => a | b;");

        await Assert
            .That(output)
            .Contains(
                "return a === true || b === true ? true : a == null || b == null ? null : false;"
            );
    }

    [Test]
    public async Task NullableBool_Xor_IsNullWhenEitherIsNull()
    {
        var output = Transpile("public static bool? Differ(bool? a, bool? b) => a ^ b;");

        await Assert.That(output).Contains("return a == null || b == null ? null : a !== b;");
    }

    [Test]
    public async Task MixedBoolAndNullableBool_OnlyTestsTheNullableOperandForNull()
    {
        var output = Transpile(
            """
            public static bool? And(bool a, bool? b) => a & b;
            public static bool? Or(bool? a, bool b) => a | b;
            public static bool? Xor(bool a, bool? b) => a ^ b;
            """
        );

        await Assert
            .That(output)
            .Contains("return !a || b === false ? false : b == null ? null : true;");
        await Assert
            .That(output)
            .Contains("return a === true || b ? true : a == null ? null : false;");
        await Assert.That(output).Contains("return b == null ? null : a !== b;");
    }

    [Test]
    public async Task NullableBool_NullLiteralOperand_NeverDecidesTheOutcome()
    {
        var output = Transpile(
            """
            public static bool? And(bool? a) => a & null;
            public static bool? Or(bool? a) => a | null;
            """
        );

        await Assert.That(output).Contains("return a === false ? false : null;");
        await Assert.That(output).Contains("return a === true ? true : null;");
    }

    [Test]
    public async Task NullableBool_SideEffectingOperands_AreBoundOnceInSourceOrder()
    {
        var output = Transpile(
            """
            public static bool? Touch(bool? v) => v;
            public static bool? Both(bool? a, bool? b) => Touch(a) & Touch(b);
            """
        );

        // The outer binding runs first: `touch(a)` is evaluated before the
        // inner arrow binds `touch(b)`, and each call appears once.
        await Assert
            .That(output)
            .Contains(
                "return ((receiver$temp$0) => ((receiver$temp$1) => receiver$temp$0 === false || receiver$temp$1 === false ? false : receiver$temp$0 == null || receiver$temp$1 == null ? null : true)(Logic.touch(b)))(Logic.touch(a));"
            );
    }

    [Test]
    public async Task Compound_OnBool_ReadsAndWritesTheTarget()
    {
        var output = Transpile(
            """
            public bool Flag;
            public void AndWith(bool b) { Flag &= b; }
            public void OrWith(bool b) { Flag |= b; }
            public void XorWith(bool b) { Flag ^= b; }
            """,
            asInstance: true
        );

        await Assert.That(output).Contains("this.flag = !!(+this.flag & +b);");
        await Assert.That(output).Contains("this.flag = !!(+this.flag | +b);");
        await Assert.That(output).Contains("this.flag = this.flag !== b;");
    }

    [Test]
    public async Task Compound_OnNullableBool_UsesThreeValuedLogic()
    {
        var output = Transpile(
            """
            public bool? Flag;
            public void AndWith(bool? b) { Flag &= b; }
            """,
            asInstance: true
        );

        await Assert
            .That(output)
            .Contains(
                "this.flag = this.flag === false || b === false ? false : this.flag == null || b == null ? null : true;"
            );
    }

    [Test]
    public async Task Compound_OnNullConditionalNullableBool_UsesThreeValuedLogic()
    {
        var output = Transpile(
            """
            public class Box { public bool? Flag; }
            public void AndWith(Box? box, bool? b) { box?.Flag &= b; }
            """,
            asInstance: true
        );

        await Assert.That(output).DoesNotContain("&=");
        await Assert.That(output).Contains("=== false");
    }

    [Test]
    public async Task Compound_OnSideEffectingElement_EvaluatesTheIndexOnce()
    {
        var output = Transpile(
            """
            public List<bool> Flags = [false, true];
            public int Cursor;
            public int Next() => Cursor++;
            public void AndWith(bool b) { Flags[Next()] &= b; }
            """,
            asInstance: true
        );

        await Assert
            .That(output)
            .Contains("this.flags[receiver$temp$0] = !!(+this.flags[receiver$temp$0] & +b)");
        await Assert.That(output.Split("this.next()").Length).IsEqualTo(2);
    }

    private static string Transpile(string members, bool asInstance = false)
    {
        var modifier = asInstance ? "" : "static ";

        var files = TranspileHelper.Transpile(
            $$"""
            #nullable enable
            using System;
            using System.Collections.Generic;
            using Metano.Annotations;

            [Transpile, Flags]
            public enum Flag { None = 0, A = 1, B = 2 }

            [Transpile]
            public {{modifier}}class Logic
            {
                {{members}}
            }
            """
        );

        return files["logic.ts"];
    }
}
