using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Local functions are not lowered (their statement reports MS0001), so a
/// reference to one has nothing to bind to: it reports MS0001 and becomes the
/// throwing placeholder instead of a call on the enclosing type that fails
/// when it runs (#677).
/// </summary>
public class LocalFunctionReferenceTests
{
    [Test]
    [Arguments("return Twice(2); static int Twice(int x) => x * 2;")]
    [Arguments("static int Twice(int x) => x * 2; return Twice(2);")]
    [Arguments("var k = 3; return Twice(2); int Twice(int x) => x + k;")]
    public async Task CallToALocalFunction_IsReportedAndNotPrintedAsAMemberCall(string body)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            [Transpile]
            public static class Local
            {
                public static int Run()
                {
                    {{body}}
                }
            }
            """
        );

        var messages = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(messages.Any(m => m.Contains("Local function 'Twice'"))).IsTrue();
        await Assert.That(files["local.ts"]).Contains("Unsupported expression: LocalFunction");
        await Assert.That(files["local.ts"]).DoesNotContain("Local.twice");
        await Assert.That(files["local.ts"]).DoesNotContain("this.twice");
    }
}
