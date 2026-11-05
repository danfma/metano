using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// When a user-declared or user-imported name in a generated module matches a
/// <c>metano-runtime</c> helper name, the runtime import is emitted under a
/// unique alias (<c>import { isInt32 as isInt32$rt }</c>) and every generated
/// reference goes through that alias (issue #484). Without the alias the
/// output either raises a TS duplicate-identifier error or — worse — the
/// helper calls silently bind to the user's declaration.
/// </summary>
public class RuntimeHelperAliasTests
{
    [Test]
    public async Task ExportedAsModuleFunctionNamedLikeHelper_AliasesTheRuntimeImport()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            [EmitInFile("shared")]
            public static class Checks
            {
                public static bool ValueEquals(int a, int b) => a == b;
            }

            [Transpile]
            [EmitInFile("shared")]
            public record Sample(Part Item);

            [Transpile]
            [EmitInFile("shared")]
            public record Part(int N);
            """
        );

        var output = result["shared.ts"];

        // The user's [NoContainer] function keeps its name…
        await Assert.That(output).Contains("export function valueEquals(");
        // …while the runtime helper is imported under the alias and every
        // generated reference binds through it.
        await Assert.That(output).Contains("valueEquals as valueEquals$rt");
        await Assert.That(output).Contains("valueEquals$rt(");
        await Assert.That(output).DoesNotContain(" valueEquals(this.");
    }

    [Test]
    public async Task TypeNamedLikeHelper_AliasesTheDispatcherTypeCheck()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class isInt32
            {
                public isInt32(int a)
                {
                    _ = a;
                }

                public isInt32(string b)
                {
                    _ = b;
                }
            }
            """
        );

        var output = result["is-int32.ts"];

        await Assert.That(output).Contains("export class isInt32");
        await Assert.That(output).Contains("isInt32 as isInt32$rt");
        await Assert.That(output).Contains("isInt32$rt(");
    }

    [Test]
    public async Task UserValueImportNamedLikeHelper_AliasesTheRuntimeImport()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            [Import("isString", from: "ext-stub")]
            public class isString
            {
                public object? Value { get; }
            }

            [EmitInFile("program")]
            public class Guesser
            {
                public Guesser(int n)
                {
                    Value = n;
                }

                public Guesser(string s)
                {
                    Value = s;
                }

                public object? Value { get; }
            }

            [EmitInFile("program")]
            public static class Program
            {
                [ModuleEntryPoint]
                public static void Main()
                {
                    _ = new Guesser(1);
                    _ = new Guesser("x");
                    _ = new isString();
                }
            }
            """
        );

        var output = result["program.ts"];

        // The user's own external binding keeps the plain name…
        await Assert.That(output).Contains(@"import { isString } from ""ext-stub"";");
        // …and the dispatcher's runtime check is aliased.
        await Assert.That(output).Contains("isString as isString$rt");
        await Assert.That(output).Contains("isString$rt(");
    }

    [Test]
    public async Task ValueEqualityOperator_AliasesTheTemplateCarriedHelper()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            [EmitInFile("shared")]
            public static class Checks
            {
                public static bool ValueEquals(int a, int b) => a == b;
            }

            [Transpile]
            [EmitInFile("shared")]
            public record Part(int N);

            [Transpile]
            [EmitInFile("shared")]
            public class Comparer
            {
                public static bool Same(Part a, Part b) => a == b;
            }
            """
        );

        var output = result["shared.ts"];

        // The `==` over records lowers to the `valueEquals` template; the
        // alias must reach the baked template text too, not just the import.
        await Assert.That(output).Contains("valueEquals as valueEquals$rt");
        await Assert.That(output).Contains("valueEquals$rt(a, b)");
        await Assert.That(output).Contains("export function valueEquals(");
    }

    [Test]
    public async Task HelperInsideLoopHeader_AliasesTheBakedText()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            [EmitInFile("num")]
            public static class NumFns
            {
                public static float Int64ToSingle(long value) => value;
            }

            [Transpile]
            [EmitInFile("num")]
            public class Looper
            {
                public float Total { get; private set; }

                public float Sum(long n)
                {
                    var total = 0.0f;
                    while (total < (float)n)
                    {
                        total += 1.5f;
                        n -= 1;
                    }

                    Total = total;
                    return total;
                }
            }
            """,
            Int64Representation.BigInt
        );

        var output = files["num.ts"];

        await Assert
            .That(output)
            .Contains(@"import { int64ToSingle as int64ToSingle$rt } from ""metano-runtime"";");
        // The loop header is baked into a raw statement before the alias is
        // known — it must still resolve to the alias, not to the user's
        // `int64ToSingle` export.
        await Assert.That(output).Contains("while (total < int64ToSingle$rt(n)) {");
        await Assert.That(output).DoesNotContain("while (total < int64ToSingle(");
    }
}
