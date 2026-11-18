using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The escape hatches (spec 011 FR-035, FR-036): <c>UnsafeCast&lt;T&gt;()</c> is
/// <c>value as unknown as T</c> with no check, helper or diagnostic, and
/// <c>AsDynamic()</c> is <c>(value as any)</c>. A member access on an
/// <c>AsDynamic()</c> result and a method group of either report <c>MS0001</c>.
/// </summary>
public class EscapeHatchTranspileTests
{
    [Test]
    public async Task EscapeHatches_AreCompileTimeCasts()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Interop;

            [Transpile]
            public record UserDto(string Name);

            [Transpile]
            public class Holder
            {
                public object Value { get; set; } = "";
            }

            [Transpile]
            public class Hatches
            {
                public UserDto Dto(object raw) => raw.UnsafeCast<UserDto>();

                public string Name(object raw) => raw.UnsafeCast<UserDto>().Name;

                public T Generic<T>(object raw) => raw.UnsafeCast<T>();

                public string? Chained(Holder? holder) => holder?.Value.UnsafeCast<UserDto>().Name;

                public int Call(object f) => f.UnsafeCast<Func<int, int>>()(2);

                public UserDto Static(object raw) => InteropExtensions.UnsafeCast<UserDto>(raw);

                public object Dynamic(Holder holder) => holder.AsDynamic();

                public object Local(Holder holder)
                {
                    var any = holder.AsDynamic();
                    return any;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("escape-hatches.ts", files["hatches.ts"]);
    }

    [Test]
    [Arguments("public object F(Holder h) => h.AsDynamic().Value;", "h.AsDynamic().Value")]
    [Arguments("public object F(Holder h) => h.AsDynamic()[0];", "h.AsDynamic()[0]")]
    [Arguments("public void F(Holder h) { h.AsDynamic().Value = 1; }", "h.AsDynamic().Value")]
    [Arguments(
        "public Func<object, dynamic> F() => InteropExtensions.AsDynamic;",
        "InteropExtensions.AsDynamic"
    )]
    [Arguments(
        "public Func<object, Holder> F() => InteropExtensions.UnsafeCast<Holder>;",
        "InteropExtensions.UnsafeCast<Holder>"
    )]
    public async Task UnsupportedUse_ReportsMs0001(string member, string located)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            "using Metano.Interop;\n\n[Transpile]\npublic class Holder { public object Value { get; set; } = \"\"; }\n\n"
                + "[Transpile]\npublic class Probe\n{\n"
                + member
                + "\n}\n"
        );

        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(unsupported.Count).IsEqualTo(1);
        await Assert.That(LocatedText(unsupported[0])).IsEqualTo(located);
    }

    private static string LocatedText(MetanoDiagnostic diagnostic)
    {
        var location = diagnostic.Location!;

        return location
            .SourceTree!.ToString()
            .Substring(location.SourceSpan.Start, location.SourceSpan.Length);
    }
}
