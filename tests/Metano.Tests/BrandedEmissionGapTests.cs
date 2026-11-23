using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The branded-type emission gaps of issues #288, #623, #626 and #634: a
/// wrapper over a non-primitive lowers as the record it is, the members a
/// primitive-backed companion cannot declare are reported, a generic branded
/// type declares its type parameters, an explicit delegate creation lowers as
/// its argument and <c>MS0063</c> names an <c>out</c> overload by its own
/// signature.
/// </summary>
public class BrandedEmissionGapTests
{
    private const string BoxedSource = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        public sealed record Inner(int X);

        [Branded]
        public readonly record struct Boxed(Inner Value)
        {
            public static Boxed Make(int x) => new Boxed(new Inner(x));

            public int Twice() => Value.X * 2;
        }

        public static class Use
        {
            public static bool Same() => new Boxed(new Inner(3)) == Boxed.Make(3);
        }
        """;

    /// <summary>
    /// <c>[Branded]</c> over a record is a class with its record members, and
    /// <c>new Boxed(x)</c> constructs it: no <c>Boxed.create</c> (#288).
    /// </summary>
    [Test]
    public async Task NonPrimitiveWrapper_LowersAsRecordClass()
    {
        var result = TranspileHelper.Transpile(BoxedSource);

        await TranspileHelper.AssertGolden("branded-non-primitive-fallback.ts", result["boxed.ts"]);
        await Assert.That(result["use.ts"]).Contains("new Boxed(new Inner(3))");
        await Assert.That(result["use.ts"]).DoesNotContain("Boxed.create");
    }

    /// <summary>
    /// A static field of the wrapper's own type no longer recurses through
    /// the type mapper (stack overflow before the fix), and it is reported
    /// with the instance method and the static property the companion object
    /// cannot declare (#623, #634 item 3).
    /// </summary>
    [Test]
    public async Task PrimitiveWrapper_ReportsMembersTheCompanionCannotDeclare()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct UId(long Value)
            {
                public static readonly UId Zero = new(0);

                public static UId One => new(1);

                public long Next() => Value + 1;

                [Ignore]
                public long Skipped() => Value;

                public override string ToString() => Value.ToString();

                public static string Tag(UId u) => "t" + u.Value;
            }
            """
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(files["u-id.ts"]).Contains("tag: UId$tag");
        await Assert.That(unsupported).Count().IsEqualTo(3);
        await Assert.That(unsupported.Any(m => m.StartsWith("'UId.Zero'"))).IsTrue();
        await Assert.That(unsupported.Any(m => m.StartsWith("'UId.One'"))).IsTrue();
        await Assert.That(unsupported.Any(m => m.StartsWith("'UId.Next'"))).IsTrue();
    }

    /// <summary>
    /// <c>new Func&lt;…&gt;(M)</c> lowers as the method group it converts, a
    /// bound instance method included, never as <c>new Object(M)</c> (#634 item 5).
    /// </summary>
    [Test]
    public async Task ExplicitDelegateCreation_LowersAsItsArgument()
    {
        var result = TranspileHelper.Transpile(
            """
            using System;
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct UId(long Value)
            {
                public static string Tag(UId u) => "t" + u.Value;
            }

            public class Holder
            {
                public int N = 2;

                public int Get() => N;

                public int Run()
                {
                    var tag = new Func<UId, string>(UId.Tag);
                    var get = new Func<int>(Get);
                    var inc = new Func<int, int>(x => x + 1);

                    return tag(new UId(1)).Length + get() + inc(1);
                }
            }
            """
        );

        await Assert.That(result["holder.ts"]).Contains("const tag = UId.tag;");
        await Assert.That(result["holder.ts"]).Contains("const get = this.get.bind(this);");
        await Assert.That(result["holder.ts"]).Contains("const inc = (x: number) => (x + 1) | 0;");
        await Assert.That(result["holder.ts"]).DoesNotContain("new Object(");
    }

    /// <summary>
    /// <c>MS0063</c> names <c>Mix(long)</c> and <c>Mix(long, out int)</c> by
    /// their own signatures instead of printing the plain one twice (#634
    /// item 6).
    /// </summary>
    [Test]
    public async Task AmbiguousGroupWithOutOverloads_NamesEachSignature()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct UId(long Value);

            [Branded]
            public readonly record struct Mx(long Value)
            {
                public static string Mix(long l) => "l";

                public static string Mix(long l, out int o) { o = 1; return "lo"; }

                public static string Mix(UId u) => "u";

                public static string Mix(UId u, out int o) { o = 2; return "uo"; }
            }
            """
        );

        var ambiguity = diagnostics.Single(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch);

        await Assert
            .That(ambiguity.Message)
            .StartsWith(
                "Overloads 'Mx.Mix(long)', 'Mx.Mix(long, out int)', 'Mx.Mix(UId)', "
                    + "'Mx.Mix(UId, out int)' share one runtime test"
            );
    }
}
