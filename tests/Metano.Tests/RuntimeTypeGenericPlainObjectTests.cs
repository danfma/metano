using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A generic <c>[PlainObject]</c> has no runtime test yet: its TypeScript
/// interface and guard are not emitted generically. <c>is</c> / <c>as</c> / a
/// switch on <c>Box&lt;int&gt;</c> report MS0001, as before spec 011 B2, and
/// never reach the bridge's structural-guard assertion; a dispatcher branch
/// over it is left untested.
/// </summary>
public class RuntimeTypeGenericPlainObjectTests
{
    [Test]
    [Arguments("public static bool Of(object o) => o is Box<int>;", "o is Box<int>")]
    [Arguments("public static Box<int>? Of(object o) => o as Box<int>;", "o as Box<int>")]
    [Arguments(
        "public static int Of(object o) => o switch { Box<int> b => 1, _ => 0 };",
        "Box<int> b"
    )]
    public async Task ATestOfAClosedGenericPlainObject_ReportsUnsupported(
        string member,
        string site
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            [Transpile, PlainObject] public record Box<T>(T Value, string Label);

            [Transpile]
            public static class Probe
            {
                {{member}}
            }
            """
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(unsupported).HasSingleItem();
        await Assert.That(unsupported[0].Message).Contains(site);
    }

    /// <summary>
    /// A cast to it and a dispatcher branch over it never crash either: the cast
    /// is only asserted, and the dispatcher's branch falls back to its arity.
    /// </summary>
    [Test]
    public async Task ACastAndADispatcherOverAClosedGenericPlainObject_DoNotCrash()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile, PlainObject] public record Box<T>(T Value, string Label);

            [Transpile]
            public static class Probe
            {
                public static Box<int> ToBox(object o) => (Box<int>)o;

                public static string Over(Box<int> b) => "box";

                public static string Over(string s) => s;
            }
            """
        );

        await Assert.That(files["probe.ts"]).Contains("return o as Box<number>;");
    }
}
