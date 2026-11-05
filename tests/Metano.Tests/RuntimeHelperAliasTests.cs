using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// When a user-declared or user-imported name in a generated module matches a
/// <c>metano-runtime</c> helper name — a top-level function, a type, a
/// parameter, a local, a LINQ pipe operator — the runtime import is emitted
/// under a unique alias (<c>import { isInt32 as isInt32$rt }</c>) and every
/// generated reference goes through that alias (issue #484). Without the
/// alias the output either raises a TS duplicate-identifier error or —
/// worse — the helper calls silently bind to the user's member.
/// </summary>
public class RuntimeHelperAliasTests
{
    /// <summary>
    /// The [ExportedAsModule] scenario from the issue: a user function named
    /// <c>ValueEquals</c> next to a record whose synthesized equals calls the
    /// runtime helper. Golden: runtime-helper-alias-exported-as-module.ts.
    /// </summary>
    [Test]
    public async Task ExportedAsModuleFunctionNamedLikeHelper_AliasesTheRuntimeImport()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            [EmitInFile("shared")]
            public static class Checks
            {
                public static bool ValueEquals(int a, int b) => a == b;
            }

            [Transpile]
            [EmitInFile("shared")]
            public record Inner(int N);

            [Transpile]
            [EmitInFile("shared")]
            public record Part(Inner Item);
            """
        );

        var output = files["shared.ts"];

        // The user's exported function keeps its name…
        await Assert.That(output).Contains("export function valueEquals(");
        // …while the runtime line carries the alias and the synthesized
        // equals binds through it.
        await Assert
            .That(output)
            .Contains(
                @"import { HashCode, copyRecordWith, valueEquals as valueEquals$rt } from ""metano-runtime"";"
            );
        await Assert
            .That(output)
            .Contains(
                "return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals$rt(this.item, other.item);"
            );
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert
            .That(files["shared.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("runtime-helper-alias-exported-as-module.ts"));
    }

    /// <summary>
    /// A user type claiming a type-check helper name: the dispatcher's
    /// runtime check aliases, the user class keeps the name.
    /// </summary>
    [Test]
    public async Task TypeNamedLikeHelper_AliasesTheDispatcherTypeCheck()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
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

        var output = files["is-int32.ts"];

        await Assert.That(output).Contains("export class isInt32");
        await Assert
            .That(output)
            .Contains(@"import { isInt32 as isInt32$rt, isString } from ""metano-runtime"";");
        await Assert.That(output).Contains("isInt32$rt(args[0])");
        // No collision for `isString` — the negative dispatcher check stays
        // on the plain name.
        await Assert.That(output).Contains("isString(args[0])");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A user value import from another module claiming a helper name: both
    /// lines survive — the user binding plain, the runtime one aliased.
    /// </summary>
    [Test]
    public async Task UserValueImportNamedLikeHelper_AliasesTheRuntimeImport()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
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

        var output = files["program.ts"];

        await Assert.That(output).Contains(@"import { isString } from ""ext-stub"";");
        await Assert
            .That(output)
            .Contains(@"import { isInt32, isString as isString$rt } from ""metano-runtime"";");
        await Assert.That(output).Contains("isString$rt(");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The `==` over records lowers to the `valueEquals` template; the alias
    /// must reach the baked template text too, not just the import.
    /// </summary>
    [Test]
    public async Task ValueEqualityOperator_AliasesTheTemplateCarriedHelper()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            [EmitInFile("shared")]
            public static class Checks
            {
                [Name("valueEquals$rt")]
                public static bool ValueEqualsRt(int a, int b) => a == b;

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

        var output = files["shared.ts"];

        // `valueEquals$rt` is itself taken by the user, so the alias tiers to
        // `$rt2` for the runtime binding.
        await Assert
            .That(output)
            .Contains(
                @"import { HashCode, copyRecordWith, valueEquals as valueEquals$rt2 } from ""metano-runtime"";"
            );
        await Assert.That(output).Contains("valueEquals$rt2(a, b)");
        await Assert.That(output).Contains("export function valueEquals(");
        await Assert.That(output).Contains("export function valueEquals$rt(");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The int64 conversion helper inside a raw-baked loop header: the
    /// deferred wrap bakes into the header text before the alias is known and
    /// still resolves to the alias, not to the user's export.
    /// </summary>
    [Test]
    public async Task HelperInsideLoopHeader_AliasesTheBakedText()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            [EmitInFile("num")]
            public static class NumFns
            {
                public static float Int64ToSingle(long value) => value;
            }

            [Transpile]
            [EmitInFile("num")]
            public class Looper
            {
                public float Sum(long n)
                {
                    var total = 0.0f;
                    while (total < (float)n)
                    {
                        total += 1.5f;
                        n -= 1;
                    }

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
        await Assert.That(output).Contains("while (total < int64ToSingle$rt(n)) {");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A parameter named like a template-carried helper: the generated
    /// division call inside the same method would bind to the parameter, so
    /// the runtime import aliases and the call routes through it.
    /// </summary>
    [Test]
    public async Task LocalOrParameterNamedLikeHelper_AliasesTheRuntimeImport()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Divider
            {
                public static int Div(int intDiv, int b) => (intDiv + b) / 2;
            }
            """
        );

        var output = files["divider.ts"];

        await Assert
            .That(output)
            .Contains(@"import { intDiv as intDiv$rt } from ""metano-runtime"";");
        await Assert.That(output).Contains("intDiv$rt(intDiv + b, 2)");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert
            .That(files["divider.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("runtime-helper-alias-local-collision.ts"));
    }

    /// <summary>
    /// A user export named like a LINQ pipe operator: the pipe import (from
    /// the tree-shakable subpath) aliases the operator, the user function
    /// keeps the plain name.
    /// </summary>
    [Test]
    public async Task LinqPipeOperatorNamedLikeHelper_AliasesThePipeImport()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Linq;

            [Transpile, ExportedAsModule]
            [EmitInFile("queries")]
            public static class Predicates
            {
                public static bool Where(int x) => x > 0;
            }

            [Transpile]
            [EmitInFile("queries")]
            public class Queries
            {
                public static int Positive(int[] xs) => xs.Where(x => x > 0).Count();
            }
            """
        );

        var output = files["queries.ts"];

        await Assert
            .That(output)
            .Contains(
                @"import { count, linq, where as where$rt } from ""metano-runtime/system/linq"";"
            );
        await Assert.That(output).Contains("where$rt(");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert
            .That(files["queries.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("runtime-helper-alias-linq-pipe.ts"));
    }

    /// <summary>
    /// A user class claiming the class-shaped runtime helper: the type
    /// annotations, the `new` and the import all alias together, so the value
    /// and type sides always agree.
    /// </summary>
    [Test]
    public async Task ClassShapedHelper_AliasesTypeAndValueReferences()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            [EmitInFile("sets")]
            [Name(TargetLanguage.TypeScript, "HashSet")]
            public class MyBucket
            {
                public bool Has(int x) => true;
            }

            [Transpile]
            [EmitInFile("sets")]
            public class Holder
            {
                public System.Collections.Generic.HashSet<int> Items { get; private set; } =
                    new System.Collections.Generic.HashSet<int>();

                public bool Add(int x)
                {
                    Items.Add(x);

                    return true;
                }
            }
            """
        );

        var output = files["sets.ts"];

        await Assert
            .That(output)
            .Contains(@"import { HashSet as HashSet$rt } from ""metano-runtime"";");
        await Assert.That(output).Contains("items: HashSet$rt<number>");
        await Assert.That(output).Contains("new HashSet$rt<number>()");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert
            .That(files["sets.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("runtime-helper-alias-class-shaped.ts"));
    }

    /// <summary>
    /// The negative case: with no colliding name in the module, the helper
    /// imports and prints under its plain name.
    /// </summary>
    [Test]
    public async Task NoCollision_ImportsHelperUnderPlainName()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
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
            """
        );

        var output = files["guesser.ts"];

        await Assert
            .That(output)
            .Contains(@"import { isInt32, isString } from ""metano-runtime"";");
        await Assert.That(output).Contains("isInt32(args[0])");
        await Assert.That(output).DoesNotContain("$rt");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A user member claiming the unaliasable runtime type: the module would
    /// emit a duplicate identifier with no alias path to repair it, so the
    /// compiler reports MS0070 pointing at [Name].
    /// </summary>
    [Test]
    public async Task UnaliasableHelperCollision_ReportsMs0070()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Temporal
            {
                public bool Now { get; set; }
            }

            [Transpile]
            [EmitInFile("events")]
            public class Schedule
            {
                public System.DateTimeOffset At { get; set; }
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.RuntimeHelperNameHijacked))
            .IsTrue();
    }

    /// <summary>
    /// A <c>Guid</c> local declared ahead of its assignment prints as a typed
    /// <c>let g: UUID;</c> — a type-only helper reference. The runtime import
    /// must appear even though no value-side helper was collected: the
    /// type-reference bucket alone drives it.
    /// </summary>
    [Test]
    public async Task GuidLocalDeclaredAheadOfAssignment_ImportsTheUuidType()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            [EmitInFile("store")]
            public static class Store
            {
                public static string Label()
                {
                    Guid g;
                    g = default;
                    return g.ToString();
                }
            }
            """
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();

        var output = files["store.ts"];
        await Assert.That(output).Contains("let g: UUID;");
        await Assert.That(output).Contains(@"import { UUID } from ""metano-runtime"";");
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
