namespace Metano.Tests;

/// <summary>
/// <c>++</c> / <c>--</c> over nullable values are lifted (#323): <c>null</c>
/// stays <c>null</c>, where the JS operators would turn it into a number.
/// Explicit conversions out of a nullable value throw like <c>.Value</c>, and
/// a null-forgiving <c>x!</c> over a reference stays a TS non-null assertion.
/// </summary>
public class NullableIncrementTests
{
    [Test]
    public async Task IncrementStatement_OnNullableLocal_IsLifted()
    {
        var output = Transpile(
            """
            public static int? Bump(int? a) { a++; return a; }
            public static int? Drop(int? a) { a--; return a; }
            """
        );

        await Assert.That(output).Contains("a = a == null ? null : a + 1;");
        await Assert.That(output).Contains("a = a == null ? null : a - 1;");
    }

    [Test]
    public async Task PrefixIncrement_YieldsTheNewValue()
    {
        var output = Transpile("public static int? Bump(int? a) => ++a;");

        await Assert.That(output).Contains("return a = a == null ? null : a + 1;");
    }

    [Test]
    public async Task PostfixIncrement_WhoseValueIsUsed_YieldsThePreviousValue()
    {
        var output = Transpile("public static int? Bump(int? a) => a++;");

        await Assert.That(output).Contains("(receiver$temp$0) => {");
        await Assert
            .That(output)
            .Contains("a = receiver$temp$0 == null ? null : receiver$temp$0 + 1;");
        await Assert.That(output).Contains("return receiver$temp$0;");
        await Assert.That(output).Contains(")(a);");
    }

    [Test]
    public async Task PostfixDecrement_OnNullableField_YieldsThePreviousValue()
    {
        var output = Transpile(
            """
            public int? Count;
            public int? Drop() => Count--;
            """,
            asInstance: true
        );

        await Assert
            .That(output)
            .Contains("this.count = receiver$temp$0 == null ? null : receiver$temp$0 - 1;");
        await Assert.That(output).Contains(")(this.count);");
    }

    [Test]
    public async Task Increment_OnSideEffectingElement_EvaluatesTheIndexOnce()
    {
        var output = Transpile(
            """
            public List<int?> Items = [null, 1];
            public int Cursor;
            public int Next() => Cursor++;
            public void Bump() { Items[Next()]++; }
            """,
            asInstance: true
        );

        await Assert
            .That(output)
            .Contains(
                "((receiver$temp$0) => this.items[receiver$temp$0] = ((receiver$temp$1) => receiver$temp$1 == null ? null : receiver$temp$1 + 1)(this.items[receiver$temp$0]))(this.next());"
            );
        await Assert.That(output.Split("this.next()").Length).IsEqualTo(2);
    }

    [Test]
    public async Task Increment_OnNonNullableValue_KeepsTheNativeOperator()
    {
        var output = Transpile(
            """
            public static int Bump(int a) { a++; ++a; return a--; }
            """
        );

        await Assert.That(output).Contains("a++;");
        await Assert.That(output).Contains("++a;");
        await Assert.That(output).Contains("return a--;");
    }

    [Test]
    public async Task CompoundAssignment_OnSideEffectingNullableTarget_EvaluatesTheTargetOnce()
    {
        var output = Transpile(
            """
            public List<int?> Items = [null, 1];
            public int Cursor;
            public int Next() => Cursor++;
            public void Add() { Items[Next()] += 2; }
            """,
            asInstance: true
        );

        await Assert
            .That(output)
            .Contains(
                "((receiver$temp$0) => this.items[receiver$temp$0] = ((receiver$temp$1) => receiver$temp$1 == null ? null : receiver$temp$1 + 2)(this.items[receiver$temp$0]))(this.next());"
            );
        await Assert.That(output.Split("this.next()").Length).IsEqualTo(2);
    }

    [Test]
    public async Task ExplicitConversion_FromNullableValue_ThrowsWhenMissing()
    {
        var output = Transpile(
            """
            public static int Plus(int? a) => (int)(a + 1);
            public static long Widen(int? a) => (long)a;
            """
        );

        await Assert.That(output).Contains("return (a == null ? null : a + 1) ?? (() => {");
        await Assert.That(output).Contains("Nullable object must have a value.");
        await Assert.That(output).Contains("return a ?? (() => {");
    }

    [Test]
    public async Task ExplicitConversion_BetweenNonNullableValues_StaysTransparent()
    {
        var output = Transpile("public static long Widen(int a) => (long)a;");

        await Assert.That(output).Contains("return a;");
    }

    [Test]
    public async Task NullForgiving_OverAReference_KeepsTheTypeScriptAssertion()
    {
        var output = Transpile(
            """
            public static int Length(string? s) => s!.Length;
            public static string Same(string? s) { var t = s!; return t; }
            """
        );

        await Assert.That(output).Contains("return s!.length;");
        await Assert.That(output).Contains("const t = s!;");
    }

    [Test]
    public async Task NullForgiving_OverNullableValueOrLiteral_PassesThrough()
    {
        var output = Transpile(
            """
            public static int? Same(int? a) => a!;
            public static string? Nothing() => null!;
            """
        );

        await Assert.That(output).Contains("return a;");
        await Assert.That(output).Contains("return null;");
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

            [Transpile]
            public {{modifier}}class Counter
            {
                {{members}}
            }
            """
        );

        return files["counter.ts"];
    }
}
