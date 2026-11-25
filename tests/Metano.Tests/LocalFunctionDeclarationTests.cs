using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// No target lowers a local function yet (FR-099): a used one reports an error at
/// its declaration, since every reference to it throws; an unused one, one only
/// a recursive call or <c>nameof</c> names, drops nothing that runs and reports
/// nothing at the declaration.
/// </summary>
public class LocalFunctionDeclarationTests
{
    [Test]
    [Arguments("return Twice(a);")]
    [Arguments("Func<int, int> f = Twice; return f(a);")]
    [Arguments("return new[] { a }.Select(Twice).First();")]
    [Arguments("Func<int> f = () => Twice(a); return f();")]
    [Arguments("Action<int> h = null!; h += x => Twice(x); return a;")]
    [Arguments("var r = Twice(a); return r;")]
    public async Task UsedLocalFunction_ReportsAnErrorAtTheDeclaration(string body)
    {
        var errors = DeclarationErrors(body, "int Twice(int x) => x * 2;");

        await Assert.That(errors).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ChainedLocalFunctions_EachReportAnError()
    {
        var errors = DeclarationErrors(
            "return Four(a);",
            "int Four(int x) => Twice(Twice(x)); int Twice(int x) => x * 2;"
        );

        await Assert.That(errors).Count().IsEqualTo(2);
    }

    [Test]
    public async Task GenericLocalFunction_ReportsAnErrorAtTheDeclaration()
    {
        var errors = DeclarationErrors("return Id<int>(a) + Id(a);", "T Id<T>(T x) => x;");

        await Assert.That(errors).Count().IsEqualTo(1);
    }

    [Test]
    public async Task EventHandlerLocalFunction_ReportsAnErrorAtTheDeclaration()
    {
        var errors = DeclarationErrors(
            "Changed += OnChanged; return a;",
            "void OnChanged(int x) { }"
        );

        await Assert.That(errors).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("return a;")]
    [Arguments("var name = nameof(Twice); return a;")]
    public async Task UnusedLocalFunction_ReportsNothing(string body)
    {
        var errors = DeclarationErrors(body, "int Twice(int x) => x <= 0 ? 0 : Twice(x - 1);");

        await Assert.That(errors).IsEmpty();
    }

    private static List<MetanoDiagnostic> DeclarationErrors(string body, string function)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            using System.Linq;

            [Transpile]
            public static class Uses
            {
                public static event Action<int>? Changed;

                public static int Run(int a)
                {
                    {{body}}
                    {{function}}
                }
            }
            """
        );

        return
        [
            .. diagnostics.Where(d =>
                d.Severity == MetanoDiagnosticSeverity.Error && d.Message.Contains("local function")
            ),
        ];
    }
}
