using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A C# 14 extension operator (#761) is a function of its static class's
/// module, named like the class-level helper of its operator kind, and every
/// application calls it; a shape the lowering leaves out reports MS0001 at the
/// declaration and at each application instead of a native JS operator.
/// </summary>
public class ExtensionOperatorTests
{
    private const string Source = """
        [Transpile]
        public sealed class Sig(int value)
        {
            public int Value { get; } = value;
        }

        [Transpile]
        public static class SigOps
        {
            extension(Sig)
            {
                public static Sig operator +(Sig a, Sig b) => new(a.Value + b.Value);

                public static Sig operator -(Sig a) => new(-a.Value);

                public static Sig operator ++(Sig a) => new(a.Value + 1);

                public static bool operator <(Sig a, Sig b) => a.Value < b.Value;

                public static bool operator >(Sig a, Sig b) => a.Value > b.Value;

                public static Sig operator *(Sig a, int k) => new(a.Value * k);

                public static Sig operator checked *(Sig a, int k) => new(checked(a.Value * k));
            }
        }

        [Transpile]
        public static class Use
        {
            public static int Run(Sig a, Sig b)
            {
                var c = a + b;
                c += a;
                ++c;
                var negated = -c;
                var scaled = checked(c * 2);
                var wrapped = unchecked(c * 2);

                return a < b && !(a > b) ? negated.Value + scaled.Value + wrapped.Value : 0;
            }
        }
        """;

    [Test]
    public async Task ExtensionOperators_AreModuleFunctionsCalledAtEachApplication()
    {
        var files = TranspileHelper.Transpile(Source);

        await Assert
            .That(files["sig-ops.ts"])
            .IsEqualTo(
                """
                import { checkedInt32 } from "metano-runtime";
                import { Sig } from "./sig.js";

                export function __add$Sig(a: Sig, b: Sig): Sig {
                  return new Sig((a.value + b.value) | 0);
                }

                export function __negate$Sig(a: Sig): Sig {
                  return new Sig(-a.value | 0);
                }

                export function __increment$Sig(a: Sig): Sig {
                  return new Sig((a.value + 1) | 0);
                }

                export function __lessThan$Sig(a: Sig, b: Sig): boolean {
                  return a.value < b.value;
                }

                export function __greaterThan$Sig(a: Sig, b: Sig): boolean {
                  return a.value > b.value;
                }

                export function __multiply$Sig(a: Sig, k: number): Sig {
                  return new Sig(Math.imul(a.value, k));
                }

                export function __checkedMultiply$Sig(a: Sig, k: number): Sig {
                  return new Sig(checkedInt32(a.value * k));
                }

                """
            );

        await Assert
            .That(files["use.ts"])
            .IsEqualTo(
                """
                import { __add$Sig, __checkedMultiply$Sig, __greaterThan$Sig, __increment$Sig, __lessThan$Sig, __multiply$Sig, __negate$Sig } from "./sig-ops.js";
                import type { Sig } from "./sig.js";

                export class Use {
                  constructor() { }

                  static run(a: Sig, b: Sig): number {
                    let c = __add$Sig(a, b);
                    c = __add$Sig(c, a);
                    c = __increment$Sig(c);
                    const negated = __negate$Sig(c);
                    const scaled = __checkedMultiply$Sig(c, 2);
                    const wrapped = __multiply$Sig(c, 2);

                    return __lessThan$Sig(a, b) && !__greaterThan$Sig(a, b) ? (negated.value + scaled.value + wrapped.value) | 0 : 0;
                  }
                }

                """
            );
    }

    [Test]
    public async Task ExtensionOperators_PassTsgo()
    {
        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            TranspileHelper.Transpile(Source),
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task GenericExtensionBlock_DeclaresTheBlockTypeParameters()
    {
        var files = TranspileHelper.Transpile(
            """
            using System.Numerics;

            [Transpile]
            public sealed class Box<T>(T value)
            {
                public T Value { get; } = value;
            }

            [Transpile]
            public static class BoxOps
            {
                extension<T>(Box<T>) where T : IAdditionOperators<T, T, T>
                {
                    public static Box<T> operator |(Box<T> a, Box<T> b) => b;
                }
            }

            [Transpile]
            public static class Use
            {
                public static int Pick(Box<int> a, Box<int> b) => (a | b).Value;
            }
            """
        );

        await Assert
            .That(files["box-ops.ts"])
            .Contains("export function __bitwiseOr$Box<T>(a: Box<T>, b: Box<T>): Box<T> {");

        await Assert.That(files["use.ts"]).Contains("return __bitwiseOr$Box(a, b).value;");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task NameOnExtensionOperator_RenamesTheFunctionWhole()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Sig(int value)
            {
                public int Value { get; } = value;
            }

            [Transpile]
            public static class SigOps
            {
                extension(Sig)
                {
                    [Name("plus")]
                    public static Sig operator +(Sig a, Sig b) => new(a.Value + b.Value);
                }
            }

            [Transpile]
            public static class Use
            {
                public static Sig Sum(Sig a, Sig b) => a + b;
            }
            """
        );

        await Assert
            .That(files["sig-ops.ts"])
            .Contains("export function __plus$Sig(a: Sig, b: Sig)");
        await Assert.That(files["use.ts"]).Contains("return __plus$Sig(a, b);");
    }

    [Test]
    public async Task ExtensionOperatorFromAReferencedPackage_IsImportedFromIt()
    {
        const string library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("sig-lib")]

            public sealed class Sig(int value)
            {
                public int Value { get; } = value;
            }

            public static class SigOps
            {
                extension(Sig)
                {
                    public static Sig operator +(Sig a, Sig b) => new(a.Value + b.Value);
                }
            }
            """;

        const string consumer = """
            [Transpile]
            public static class Use
            {
                public static Sig Sum(Sig a, Sig b) => a + b;
            }
            """;

        foreach (
            var files in new[]
            {
                TranspileHelper.TranspileWithLibrary(library, consumer),
                TranspileHelper.TranspileWithLibraryImage(library, consumer),
            }
        )
        {
            await Assert
                .That(files["use.ts"])
                .IsEqualTo(
                    """
                    import { __add$Sig, type Sig } from "sig-lib";

                    export class Use {
                      constructor() { }

                      static sum(a: Sig, b: Sig): Sig {
                        return __add$Sig(a, b);
                      }
                    }

                    """
                );
        }
    }

    [Test]
    public async Task UnsupportedExtensionOperators_ReportMs0001AtDeclarationAndUse()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public sealed class Sig(int value)
            {
                public int Value { get; set; } = value;
            }

            [Transpile]
            public static class Ops
            {
                extension(Sig)
                {
                    public static Sig operator +(Sig a, Sig b) => new(a.Value + b.Value);

                    public static Sig operator +(Sig a, int k) => new(a.Value + k);

                    public static bool operator true(Sig a) => a.Value != 0;

                    public static bool operator false(Sig a) => a.Value == 0;
                }

                extension(Sig s)
                {
                    public void operator -=(Sig other) => s.Value -= other.Value;
                }
            }

            [Transpile]
            public static class Use
            {
                public static int Run(Sig a, Sig b)
                {
                    var sum = a + b;
                    var shifted = a + 1;
                    a -= b;

                    return sum.Value + shifted.Value;
                }
            }
            """
        );

        var messages = UnsupportedMessages(diagnostics);

        await Assert
            .That(messages.Count(m => m.Contains("more than one extension 'op_Addition'")))
            .IsEqualTo(4);

        await Assert
            .That(messages.Count(m => m.Contains("'true' / 'false' operator")))
            .IsEqualTo(2);

        await Assert
            .That(messages.Count(m => m.Contains("instance extension operator")))
            .IsEqualTo(2);

        await Assert.That(files.GetValueOrDefault("ops.ts", "")).DoesNotContain("__add");
        await Assert.That(files["use.ts"]).DoesNotContain("a + b");
        await Assert.That(files["use.ts"]).DoesNotContain("a + 1");
        await Assert.That(files["use.ts"]).DoesNotContain("a -= b");
    }

    [Test]
    public async Task OverloadsWithDistinctNames_AreEachAFunction()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Sig(int value)
            {
                public int Value { get; } = value;
            }

            [Transpile]
            public static class SigOps
            {
                extension(Sig)
                {
                    [Name("plusSig")]
                    public static Sig operator +(Sig a, Sig b) => new(a.Value + b.Value);

                    [Name("plusInt")]
                    public static Sig operator +(Sig a, int k) => new(a.Value + k);
                }
            }

            [Transpile]
            public static class Use
            {
                public static int Run(Sig a, Sig b) => (a + b + 1).Value;
            }
            """
        );

        await Assert
            .That(files["sig-ops.ts"])
            .Contains("export function __plusSig$Sig(a: Sig, b: Sig)");
        await Assert
            .That(files["sig-ops.ts"])
            .Contains("export function __plusInt$Sig(a: Sig, k: number)");
        await Assert
            .That(files["use.ts"])
            .Contains("return __plusInt$Sig(__plusSig$Sig(a, b), 1).value;");
    }

    [Test]
    public async Task TwoClassesOfOneNamespace_ExportReceiverQualifiedNames()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace Demo
            {
                [Transpile]
                public sealed class Sig(int value)
                {
                    public int Value { get; } = value;
                }

                [Transpile]
                public sealed class Other(int value)
                {
                    public int Value { get; } = value;
                }

                [Transpile]
                public static class SigOps
                {
                    extension(Sig)
                    {
                        public static Sig operator *(Sig a, Sig b) => new(a.Value * b.Value);
                    }
                }

                [Transpile]
                public static class OtherOps
                {
                    extension(Other)
                    {
                        public static Other operator *(Other a, Other b) => new(a.Value * b.Value);
                    }
                }

                [Transpile]
                public static class Use
                {
                    public static int Run(Sig s, Other o) => (s * s).Value + (o * o).Value;
                }
            }
            """
        );

        await Assert
            .That(files["demo/index.ts"])
            .Contains("export { __multiply$Other } from \"./other-ops.js\";");
        await Assert
            .That(files["demo/index.ts"])
            .Contains("export { __multiply$Sig } from \"./sig-ops.js\";");
        await Assert
            .That(files["demo/use.ts"])
            .Contains("__multiply$Sig(s, s).value + __multiply$Other(o, o).value");

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            files,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task OneReceiverInTwoClasses_ReportsMs0021AtTheDeclaration()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public sealed class Sig(int value)
            {
                public int Value { get; } = value;
            }

            [Transpile]
            public static class SigOps
            {
                extension(Sig)
                {
                    public static Sig operator *(Sig a, Sig b) => new(a.Value * b.Value);
                }
            }

            [Transpile]
            public static class MoreSigOps
            {
                extension(Sig)
                {
                    public static Sig operator *(Sig a, Sig b) => new(a.Value * b.Value * 2);
                }
            }
            """
        );

        var clash = diagnostics
            .Where(d => d.Code == DiagnosticCodes.ExtensionHelperNameClash)
            .ToList();

        await Assert.That(clash.Count).IsEqualTo(1);
        await Assert.That(clash[0].Message).Contains("'__multiply$Sig'");
        await Assert.That(clash[0].Location).IsNotNull();
    }

    [Test]
    public async Task OperatorOfAClassNotEmitted_ReportsMs0001AtEachUse()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public sealed class Sig(int value)
            {
                public int Value { get; } = value;
            }

            [Transpile]
            [Ignore]
            public static class HiddenOps
            {
                extension(Sig)
                {
                    public static Sig operator -(Sig a, Sig b) => new(a.Value - b.Value);
                }
            }

            [Transpile]
            public static class SigOps
            {
                extension(Sig)
                {
                    [Ignore]
                    public static Sig operator *(Sig a, Sig b) => new(a.Value * b.Value);

                    public static Sig operator +(Sig a, Sig b) => new(a.Value + b.Value);
                }
            }

            [Transpile]
            public static class Use
            {
                public static int Run(Sig a, Sig b) => (a - b).Value + (a * b).Value + (a + b).Value;
            }
            """
        );

        var messages = UnsupportedMessages(diagnostics);

        await Assert
            .That(messages.Count(m => m.Contains("'HiddenOps' is not emitted")))
            .IsEqualTo(1);
        await Assert
            .That(messages.Count(m => m.Contains("the operator is [Ignore]d")))
            .IsEqualTo(1);
        await Assert.That(files["sig-ops.ts"]).DoesNotContain("__multiply");
        await Assert.That(files["use.ts"]).DoesNotContain("a - b");
        await Assert.That(files["use.ts"]).DoesNotContain("a * b");
        await Assert.That(files["use.ts"]).Contains("__add$Sig(a, b)");
    }

    [Test]
    public async Task OperatorOfANonTranspiledLibrary_ReportsMs0001AtEachUse()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            """
            public sealed class Sig(int value)
            {
                public int Value { get; } = value;
            }

            public static class SigOps
            {
                extension(Sig)
                {
                    public static Sig operator +(Sig a, Sig b) => new(a.Value + b.Value);
                }
            }
            """,
            """
            [Transpile]
            public static class Use
            {
                public static int Sum(Sig a, Sig b) => (a + b).Value;
            }
            """
        );

        await Assert
            .That(
                UnsupportedMessages(diagnostics).Count(m => m.Contains("'SigOps' is not emitted"))
            )
            .IsEqualTo(1);

        await Assert.That(files["use.ts"]).DoesNotContain("a + b");
    }

    private static List<string> UnsupportedMessages(IEnumerable<MetanoDiagnostic> diagnostics) =>
        [
            .. diagnostics
                .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
                .Select(d => d.Message),
        ];
}
