namespace Metano.Tests;

/// <summary>
/// Element reads under <c>noUncheckedIndexedAccess</c> (baseline FR-069,
/// #670): an array, <c>List&lt;T&gt;</c> or <c>string</c> read is typed as C#
/// types it. <c>a[i]!</c> for a <c>T</c> element, <c>(a[i] ?? null)</c> for a
/// nullable one (a <c>!</c> would strip its own <c>null</c>); a write target
/// stays assignable; the final step of a native optional chain stays
/// unchecked. The emitted files were type-checked with <c>tsgo --strict
/// --noUncheckedIndexedAccess</c> and the writes ran under Bun against .NET's
/// results (<c>a[i]++; ++a[i+1]; a[i] -= 1</c> on <c>{1,2,3}</c> gives
/// <c>[1,3,3]</c> and <c>-a[0] + a[i] * 2</c> is <c>1</c>; a <c>long</c> element
/// <c>+= 3; ++</c> from 5 reads back 9).
/// </summary>
public class ElementAccessTypingTests
{
    [Test]
    [Arguments("int First(IReadOnlyList<int> values) => values[0];", "return values[0]!;")]
    [Arguments("int FromArray(int[] values, int i) => values[i];", "return values[i]!;")]
    [Arguments("char Ch(string text, int i) => text[i];", "return text[i]!;")]
    [Arguments("int Last(List<int> values) => values[^1];", "return values[values.length - 1]!;")]
    [Arguments("string[] Row(string[][] grid) => grid[0];", "return grid[0]!;")]
    [Arguments("int Nested(int[][] grid) => grid[0][1];", "return grid[0]![1]!;")]
    public async Task ElementRead_IsAsserted(string method, string expected)
    {
        await Assert.That(Emit(method)).Contains(expected);
    }

    [Test]
    public async Task NullableElementRead_IsNormalizedNotAsserted()
    {
        var output = Emit(
            """
            string? Maybe(string?[] values) => values[0];
            string OrDefault(string?[] values) => values[0] ?? "d";
            """
        );

        await Assert.That(output).Contains("return values[0] ?? null;");
        await Assert.That(output).Contains("return values[0] ?? \"d\";");
        await Assert.That(output).DoesNotContain("?? null ??");
    }

    [Test]
    public async Task WriteTargets_StayAssignable_AndReadModifyWritesAssertTheRead()
    {
        var output = Emit(
            """
            void Write(int[] a, string?[] s, int i)
            {
                a[i] = 1;
                s[i] ??= "x";
                a[i] += 2;
                a[i]++;
                --a[i + 1];
            }
            """
        );

        await Assert.That(output).Contains("a[i] = 1;");
        await Assert.That(output).Contains("s[i] ??= \"x\";");
        await Assert.That(output).Contains("a[i] = (a[i]! + 2) | 0;");
        await Assert.That(output).Contains("a[i] = (a[i]! + 1) | 0;");
        await Assert.That(output).Contains("a[(i + 1) | 0] = (a[(i + 1) | 0]! - 1) | 0;");
    }

    [Test]
    public async Task OutDestination_StaysAssignable()
    {
        var output = Emit("void Parse(int[] a, string s) { int.TryParse(s, out a[0]); }");

        await Assert.That(output).Contains("a[0] = $out0;");
    }

    [Test]
    public async Task NativeOptionalChain_KeepsItsFinalElementUnchecked()
    {
        var output = Emit(
            """
            int? First(int[]? values) => values?[0];
            int? Length(string[]? names) => names?[0].Length;
            """
        );

        await Assert.That(output).Contains("return values?.[0] ?? null;");
        await Assert.That(output).Contains("return names == null ? null : names[0]!.length;");
    }

    [Test]
    public async Task DictionaryRead_IsNotAsserted()
    {
        var output = Emit("string Get(Dictionary<string, string> d) => d[\"k\"];");

        await Assert.That(output).Contains("return d.get(\"k\");");
    }

    private static string Emit(string members) =>
        TranspileHelper.Transpile(
            $$"""
            using System.Collections.Generic;

            [Transpile]
            public class Reads
            {
                {{members}}
            }
            """
        )["reads.ts"];
}
