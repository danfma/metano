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

                export function __add(a: Sig, b: Sig): Sig {
                  return new Sig((a.value + b.value) | 0);
                }

                export function __negate(a: Sig): Sig {
                  return new Sig(-a.value | 0);
                }

                export function __increment(a: Sig): Sig {
                  return new Sig((a.value + 1) | 0);
                }

                export function __lessThan(a: Sig, b: Sig): boolean {
                  return a.value < b.value;
                }

                export function __greaterThan(a: Sig, b: Sig): boolean {
                  return a.value > b.value;
                }

                export function __multiply(a: Sig, k: number): Sig {
                  return new Sig(Math.imul(a.value, k));
                }

                export function __checkedMultiply(a: Sig, k: number): Sig {
                  return new Sig(checkedInt32(a.value * k));
                }

                """
            );

        await Assert
            .That(files["use.ts"])
            .IsEqualTo(
                """
                import { __add, __checkedMultiply, __greaterThan, __increment, __lessThan, __multiply, __negate } from "./sig-ops.js";
                import type { Sig } from "./sig.js";

                export class Use {
                  constructor() { }

                  static run(a: Sig, b: Sig): number {
                    let c = __add(a, b);
                    c = __add(c, a);
                    c = __increment(c);
                    const negated = __negate(c);
                    const scaled = __checkedMultiply(c, 2);
                    const wrapped = __multiply(c, 2);

                    return __lessThan(a, b) && !__greaterThan(a, b) ? (negated.value + scaled.value + wrapped.value) | 0 : 0;
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
            .Contains("export function __bitwiseOr<T>(a: Box<T>, b: Box<T>): Box<T> {");

        await Assert.That(files["use.ts"]).Contains("return __bitwiseOr(a, b).value;");

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

        await Assert.That(files["sig-ops.ts"]).Contains("export function __plus(a: Sig, b: Sig)");
        await Assert.That(files["use.ts"]).Contains("return __plus(a, b);");
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
                    import { __add, type Sig } from "sig-lib";

                    export class Use {
                      constructor() { }

                      static sum(a: Sig, b: Sig): Sig {
                        return __add(a, b);
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
            public sealed class Vec(int value)
            {
                public int Value { get; } = value;
            }

            [Transpile]
            public static class Ops
            {
                extension(Sig)
                {
                    public static Sig operator +(Sig a, Sig b) => new(a.Value + b.Value);

                    public static bool operator true(Sig a) => a.Value != 0;

                    public static bool operator false(Sig a) => a.Value == 0;
                }

                extension(Vec)
                {
                    public static Vec operator +(Vec a, Vec b) => new(a.Value + b.Value);
                }

                extension(Sig s)
                {
                    public void operator -=(Sig other) => s.Value -= other.Value;
                }
            }

            [Transpile]
            public static class Use
            {
                public static int Run(Sig a, Sig b, Vec v)
                {
                    var sum = a + b;
                    var vectors = v + v;
                    a -= b;

                    return sum.Value + vectors.Value;
                }
            }
            """
        );

        var messages = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert
            .That(messages.Count(m => m.Contains("declares more than one extension 'op_Addition'")))
            .IsEqualTo(4);

        await Assert
            .That(messages.Count(m => m.Contains("'true' / 'false' operator")))
            .IsEqualTo(2);

        await Assert
            .That(messages.Count(m => m.Contains("instance extension operator")))
            .IsEqualTo(2);

        await Assert.That(files.GetValueOrDefault("ops.ts", "")).DoesNotContain("__add");
        await Assert.That(files["use.ts"]).DoesNotContain("a + b");
        await Assert.That(files["use.ts"]).DoesNotContain("v + v");
        await Assert.That(files["use.ts"]).DoesNotContain("a -= b");
    }
}
