using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// BCL members that had no mapping and printed a reference nothing declares (#686, #584,
/// #403): <c>KeyValuePair.Create</c>, the <c>BigInteger</c> constants, the <c>char.Is*</c>
/// tests, <c>Convert.ToDouble</c>, <c>EqualityComparer&lt;T&gt;.Default</c>, the numeric
/// <c>CompareTo</c>, <c>StringBuilder</c>, <c>Lazy&lt;T&gt;</c> and
/// <c>TaskCompletionSource&lt;T&gt;</c>. Each lowers to a plain expression or a
/// <c>metano-runtime</c> helper and reports nothing.
/// </summary>
public class BclMemberMappingTests
{
    [Test]
    public async Task KeyValuePairCreate_BuildsTheKeyValueTuple()
    {
        var output = Transpile(
            "public static KeyValuePair<string, int> Pair() => KeyValuePair.Create(\"z\", 26);"
        );

        await Assert.That(output).Contains("return [\"z\", 26] as [string, number];");
        await Assert.That(output).DoesNotContain("KeyValuePair");
    }

    [Test]
    public async Task BigIntegerConstantsAndStateTests_LowerToBigIntLiterals()
    {
        var output = Transpile(
            """
            public static BigInteger Sum() => BigInteger.Zero + BigInteger.One + BigInteger.MinusOne;
            public static bool Flags(BigInteger b) => b.IsZero || b.IsOne || b.IsEven;
            """
        );

        await Assert.That(output).Contains("return 0n + 1n + BigInt(-1);");
        await Assert.That(output).Contains("return (b === 0n) || (b === 1n) || (b % 2n === 0n);");
    }

    [Test]
    [Arguments("IsLetter", "charIsLetter")]
    [Arguments("IsDigit", "charIsDigit")]
    [Arguments("IsLetterOrDigit", "charIsLetterOrDigit")]
    [Arguments("IsNumber", "charIsNumber")]
    [Arguments("IsUpper", "charIsUpper")]
    [Arguments("IsLower", "charIsLower")]
    [Arguments("IsPunctuation", "charIsPunctuation")]
    [Arguments("IsSymbol", "charIsSymbol")]
    [Arguments("IsSeparator", "charIsSeparator")]
    [Arguments("IsControl", "charIsControl")]
    [Arguments("IsWhiteSpace", "charIsWhiteSpace")]
    public async Task CharCategoryTest_LowersToItsRuntimeHelper(string method, string helper)
    {
        var output = Transpile($"public static bool Test(char c) => char.{method}(c);");

        await Assert.That(output).Contains($"return {helper}(c);");
        await Assert.That(output).Contains($"import {{ {helper} }} from \"metano-runtime\";");
    }

    [Test]
    public async Task CharCategoryTestOverAStringIndex_StaysUnmapped()
    {
        var diagnostics = Diagnostics("public static bool Test(string s) => char.IsLetter(s, 0);");

        await Assert.That(diagnostics.Any(IsUnmappedReport)).IsTrue();
    }

    [Test]
    public async Task ConvertToDouble_LowersByTheSourceType()
    {
        var output = Transpile(
            """
            public static double FromInt(int i) => Convert.ToDouble(i);
            public static double FromDecimal(decimal d) => Convert.ToDouble(d);
            public static double FromBool(bool b) => Convert.ToDouble(b);
            public static double FromText(string s) => Convert.ToDouble(s);
            public static double FromDecimalMethod(decimal d) => decimal.ToDouble(d);
            """
        );

        await Assert.That(output).Contains("return i;");
        await Assert.That(output).Contains("return d.toNumber();");
        await Assert.That(output).Contains("return (b ? 1 : 0);");
        await Assert.That(output).Contains("return convertStringToDouble(s);");
    }

    [Test]
    public async Task EqualityComparerDefault_IsTheRuntimeDefaultComparer()
    {
        var output = Transpile(
            """
            public static bool Same(int a, int b) => EqualityComparer<int>.Default.Equals(a, b);
            public static IEqualityComparer<string> Comparer() => EqualityComparer<string>.Default;
            """
        );

        await Assert.That(output).Contains("return defaultEqualityComparerFor().equals(a, b);");
        await Assert.That(output).Contains("return defaultEqualityComparerFor();");
    }

    [Test]
    [Arguments("int", "compareNumber(a, b)")]
    [Arguments("uint", "compareNumber(a, b)")]
    [Arguments("double", "compareNumber(a, b)")]
    [Arguments("float", "compareNumber(a, b)")]
    [Arguments("short", "(a - b)")]
    [Arguments("ushort", "(a - b)")]
    [Arguments("byte", "(a - b)")]
    [Arguments("sbyte", "(a - b)")]
    [Arguments("char", "(a.charCodeAt(0) - b.charCodeAt(0))")]
    public async Task NumericCompareTo_LowersAsDotNetReturnsIt(string type, string expected)
    {
        var output = Transpile($"public static int Compare({type} a, {type} b) => a.CompareTo(b);");

        await Assert.That(output).Contains($"return {expected}");
    }

    [Test]
    public async Task StringBuilder_LowersToTheRuntimeClass()
    {
        var output = Transpile(
            """
            public static string Build(double d, bool flag, long l, object o, char c)
            {
                var sb = new StringBuilder("x");

                sb.Append(d).Append(flag).Append(l).Append(o).Append(c).Append('z', 3).AppendLine("y");
                sb.Insert(0, "i").Remove(0, 1).Replace("a", "b");
                sb.Length = 2;

                return sb.ToString() + sb.Length + sb.ToString(0, 1);
            }
            """
        );

        await Assert.That(output).Contains("const sb = new StringBuilder(\"x\");");

        await Assert
            .That(output)
            .Contains(
                "sb.append(formatDouble(d)).append(formatBoolean(flag)).append(String(l)).append(formatText(o)).append(c).appendRepeated(\"z\", 3).appendLine(\"y\");"
            );

        await Assert
            .That(output)
            .Contains("sb.insert(0, \"i\").remove(0, 1).replace(\"a\", \"b\");");

        await Assert.That(output).Contains("sb.length = 2;");
        await Assert.That(output).Contains("sb.toString() + sb.length + sb.toSubstring(0, 1)");
        await Assert.That(output).Contains("StringBuilder");
    }

    [Test]
    public async Task Lazy_LowersToTheRuntimeClass()
    {
        var output = Transpile(
            """
            public static int Read()
            {
                var lazy = new Lazy<int>(() => 5);

                return lazy.IsValueCreated ? 0 : lazy.Value;
            }
            """
        );

        await Assert.That(output).Contains("const lazy = new Lazy<number>(() => 5);");
        await Assert.That(output).Contains("lazy.isValueCreated ? 0 : lazy.value");
    }

    [Test]
    public async Task TaskCompletionSource_LowersToTheRuntimeClass()
    {
        var output = Transpile(
            """
            public static async Task<int> Complete()
            {
                var source = new TaskCompletionSource<int>();

                source.SetResult(3);
                source.TrySetException(new InvalidOperationException());

                return await source.Task;
            }
            """
        );

        await Assert.That(output).Contains("const source = new TaskCompletionSource<number>();");
        await Assert.That(output).Contains("source.setResult(3);");

        await Assert
            .That(output)
            .Contains("source.trySetException(new InvalidOperationException());");

        await Assert.That(output).Contains("return await source.task;");
    }

    [Test]
    public async Task EveryMappedMember_ReportsNothing()
    {
        var diagnostics = Diagnostics(
            """
            public static object All(char c, int i, BigInteger b, decimal d)
            {
                var sb = new StringBuilder();

                return new object[]
                {
                    KeyValuePair.Create(1, 2),
                    BigInteger.Zero,
                    char.IsLetter(c),
                    Convert.ToDouble(i),
                    EqualityComparer<int>.Default,
                    i.CompareTo(1),
                    sb.Append(i).ToString(),
                    new Lazy<int>(() => i).Value,
                    new TaskCompletionSource<int>().Task,
                };
            }
            """
        );

        await Assert.That(diagnostics.Where(IsUnmappedReport)).IsEmpty();
    }

    [Test]
    public async Task Dart_KeepsItsOutputForTheseMembers()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            "using Metano.Annotations;\n"
                + Source("public static int Compare(int a, int b) => a.CompareTo(b);")
        );

        await Assert.That(files["bcl.dart"]).Contains("a.compareTo(b)");
    }

    private static bool IsUnmappedReport(MetanoDiagnostic diagnostic) =>
        diagnostic.Code == DiagnosticCodes.UnsupportedFeature;

    private static string Transpile(string members) =>
        TranspileHelper.Transpile(Source(members))["bcl.ts"];

    private static IReadOnlyList<MetanoDiagnostic> Diagnostics(string members) =>
        TranspileHelper.TranspileWithDiagnostics(Source(members)).Diagnostics;

    private static string Source(string members) =>
        $$"""
            using System.Collections.Generic;
            using System.Numerics;
            using System.Text;
            using System.Threading.Tasks;

            [Transpile]
            public static class Bcl
            {
            {{members}}
            }
            """;
}
