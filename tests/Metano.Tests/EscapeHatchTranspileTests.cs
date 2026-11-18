using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The escape hatches (spec 011 FR-035, FR-036): <c>UnsafeCast&lt;T&gt;()</c> is
/// <c>value as unknown as T</c> with no check, helper or diagnostic, and
/// <c>AsDynamic()</c> is <c>(value as any)</c>. A member access on an
/// <c>AsDynamic()</c> result is a <c>dynamic</c> access with its name verbatim,
/// warned by <c>MS0057</c> when the receiver's type emits that member under
/// another name; a method group of either reports <c>MS0001</c>.
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

    [Test]
    public async Task AsDynamicAccess_IsADynamicAccessWithVerbatimNames()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Interop;

            [Transpile]
            public class Holder
            {
                public int count;
            }

            [Transpile]
            public class Access
            {
                public void All(Holder h, Holder? maybe)
                {
                    var read = h.AsDynamic().count;
                    var extra = h.AsDynamic().Extra;
                    var called = h.AsDynamic().Run(1, "x");
                    var item = h.AsDynamic()[0];
                    var optional = maybe.AsDynamic()?.count;
                    h.AsDynamic().count = 2;
                    h.AsDynamic().count += 3;
                    h.AsDynamic().count++;
                    h.AsDynamic()[1] = 4;
                    var viaStatic = InteropExtensions.AsDynamic(h).count;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden("as-dynamic-access.ts", files["access.ts"]);
    }

    [Test]
    [Arguments("h.AsDynamic().Total")]
    [Arguments("h.AsDynamic().Label")]
    [Arguments("h.AsDynamic().Describe()")]
    [Arguments("h.AsDynamic().Delete()")]
    [Arguments("InteropExtensions.AsDynamic(h).Total")]
    [Arguments("h.AsDynamic().Inherited")]
    [Arguments("h.AsDynamic().Hidden")]
    [Arguments("h.AsDynamic().Pick(1)")]
    [Arguments("h.Text.AsDynamic().Length")]
    public async Task AccessToARenamedMember_ReportsMs0057(string site)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            RenamedMemberSource($"public object F(Holder h) => {site};")
        );

        var warning = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.DynamicAccessToRenamedMember
        );

        // The emitted name is not quoted: an overload suffix, an [Ignore] or a
        // BCL mapping can make the symbol's spelling differ from it.
        await Assert.That(warning.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(warning.Message).Contains("under a different name");
        await Assert.That(warning.Message).DoesNotContain(" as '");
        await Assert.That(site).StartsWith(LocatedText(warning));
        await Assert.That(files["probe.ts"]).Contains(" as any)");
    }

    [Test]
    [Arguments("h.AsDynamic().count")]
    [Arguments("h.AsDynamic().Missing")]
    [Arguments("h.AsDynamic().ToString()")]
    public async Task AccessToASameSpelledOrUnknownMember_ReportsNothing(string site)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            RenamedMemberSource($"public object F(Holder h) => {site};")
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.DynamicAccessToRenamedMember))
            .IsEmpty();
    }

    [Test]
    [Arguments("Delete", "delete")]
    [Arguments("Default", "default_")]
    [Arguments("GetHashCode", "hashCode")]
    [Arguments("Label", "caption")]
    public async Task DeclaredNameOf_FollowsTheDeclarationRule(string member, string declared)
    {
        // Methods take the method rule (no reserved-word escape, `hashCode` for
        // the GetHashCode override); data members the data-member rule.
        var compilation = TranspileHelper.CompileLibrary(
            """
            public class Shapes
            {
                public void Delete() { }

                public int Default { get; set; }

                public override int GetHashCode() => 0;

                [Name("caption")] public string Label { get; set; } = "";
            }
            """
        );

        var symbol = compilation.GetTypeByMetadataName("Shapes")!.GetMembers(member).First();

        await Assert
            .That(Metano.Compiler.TypeScript.Bridge.IrToTsNamingPolicy.DeclaredNameOf(symbol))
            .IsEqualTo(declared);
    }

    [Test]
    public async Task DartTarget_AsDynamicAccess_KeepsItsMs0001()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            RenamedMemberSource("public object F(Holder h) => h.AsDynamic().Total;")
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEqualTo(1);

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.DynamicAccessToRenamedMember))
            .IsEmpty();
    }

    private static string RenamedMemberSource(string member) =>
        "using Metano.Interop;\n\n"
        + "[Transpile]\npublic class Base { public int Inherited { get; set; } }\n\n"
        + "[Transpile]\npublic class Holder : Base\n{\n"
        + "    public int Total { get; set; }\n"
        + "    public int count;\n"
        + "    [Name(\"caption\")] public string Label { get; set; } = \"\";\n"
        + "    public string Describe() => \"\";\n"
        + "    public void Delete() { }\n"
        + "    [Ignore] public int Hidden { get; set; }\n"
        + "    [Name(\"pickOne\")] public int Pick(int x) => x;\n"
        + "    public int Pick(string s) => 0;\n"
        + "    public string Text { get; set; } = \"\";\n"
        + "}\n\n"
        + "[Transpile]\npublic class Probe\n{\n"
        + member
        + "\n}\n";

    private static string LocatedText(MetanoDiagnostic diagnostic)
    {
        var location = diagnostic.Location!;

        return location
            .SourceTree!.ToString()
            .Substring(location.SourceSpan.Start, location.SourceSpan.Length);
    }
}
