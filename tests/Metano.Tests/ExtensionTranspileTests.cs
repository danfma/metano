namespace Metano.Tests;

public class ExtensionTranspileTests
{
    // ─── Classic extension methods ──────────────────────────

    [Test]
    public async Task ClassicExtension_ReceiverBecomesFirstParam()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class StringExt
            {
                public static string Upper(this string s) => s.ToUpper();
            }
            """
        );

        var output = result["string-ext.ts"];
        await Assert.That(output).Contains("export function upper(s: string): string");
    }

    [Test]
    public async Task ClassicExtension_MultipleParams()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class MathExt
            {
                public static int Add(this int x, int y) => x + y;
            }
            """
        );

        var output = result["math-ext.ts"];
        await Assert.That(output).Contains("export function add(x: number, y: number): number");
    }

    [Test]
    public async Task ClassicExtension_AutoDetectedAsModule()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Helpers
            {
                public static int Double(this int x) => x * 2;
                public static int Triple(this int x) => x * 3;
            }
            """
        );

        var output = result["helpers.ts"];
        // Should be module-style (top-level functions), not a class
        await Assert.That(output).DoesNotContain("class Helpers");
        await Assert.That(output).Contains("export function");
    }

    [Test]
    public async Task ClassicExtension_GenericReceiver()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class EnumerableExt
            {
                public static bool IsEmpty<T>(this System.Collections.Generic.IEnumerable<T> source)
                {
                    return false;
                }
            }
            """
        );

        var output = result["enumerable-ext.ts"];
        await Assert.That(output).Contains("function isEmpty<T>(source: Iterable<T>): boolean");
    }

    // ─── C# 14 extension blocks ─────────────────────────────

    [Test]
    public async Task ExtensionBlock_WithMethod_GeneratesFunction()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class IntExtensions
            {
                extension(int value)
                {
                    public int Squared() => value * value;
                }
            }
            """
        );

        var output = result["int-extensions.ts"];
        await Assert.That(output).Contains("export function squared(value: number): number");
        await Assert.That(output).Contains("value * value");
    }

    [Test]
    public async Task ExtensionBlock_WithProperty_GeneratesFunction()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class IntExtensions
            {
                extension(int value)
                {
                    public bool IsEven => value % 2 == 0;
                }
            }
            """
        );

        var output = result["int-extensions.ts"];
        await Assert.That(output).Contains("export function isEven(value: number): boolean");
        await Assert.That(output).Contains("value % 2 === 0");
    }

    [Test]
    public async Task ExtensionBlock_MultipleMembers()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class StringExtensions
            {
                extension(string text)
                {
                    public bool IsBlank => string.IsNullOrWhiteSpace(text);
                    public string Repeat(int times) => string.Concat(System.Linq.Enumerable.Repeat(text, times));
                }
            }
            """
        );

        var output = result["string-extensions.ts"];
        await Assert.That(output).Contains("export function isBlank(text: string): boolean");
        await Assert
            .That(output)
            .Contains("export function repeat(text: string, times: number): string");
    }
}
