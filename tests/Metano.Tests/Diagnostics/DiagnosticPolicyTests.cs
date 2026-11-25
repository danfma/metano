using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Diagnostics;

/// <summary>Issue #785: the CLI values that mirror MSBuild's warning settings.</summary>
public class DiagnosticPolicyTests
{
    private static MetanoDiagnostic Warning(string code) =>
        new(MetanoDiagnosticSeverity.Warning, code, "text");

    private static DiagnosticPolicy Parse(
        bool warnAsError = false,
        string? asErrors = null,
        string? notAsErrors = null,
        string? noWarn = null
    ) => DiagnosticPolicy.FromCommandLine(warnAsError, asErrors, notAsErrors, noWarn);

    [Test]
    public async Task DefaultPolicy_LeavesWarningsAlone()
    {
        var result = Parse().Apply([Warning("MS0001")]);

        await Assert.That(result.Single().Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
    }

    [Test]
    public async Task WarnAsError_EscalatesEveryWarning_ButNotInfo()
    {
        var info = new MetanoDiagnostic(MetanoDiagnosticSeverity.Info, "MS0099", "text");

        var result = Parse(warnAsError: true).Apply([Warning("MS0001"), info]);

        await Assert.That(result[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(result[1].Severity).IsEqualTo(MetanoDiagnosticSeverity.Info);
    }

    [Test]
    public async Task WarningsAsErrors_EscalatesOnlyTheListedCodes()
    {
        var result = Parse(asErrors: "MS0001").Apply([Warning("MS0001"), Warning("MS0002")]);

        await Assert.That(result[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(result[1].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
    }

    [Test]
    public async Task WarningsNotAsErrors_Wins_OverBothEscalatingSettings()
    {
        var result = Parse(warnAsError: true, asErrors: "MS0001", notAsErrors: "MS0001")
            .Apply([Warning("MS0001"), Warning("MS0002")]);

        await Assert.That(result[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(result[1].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    [Test]
    public async Task NoWarn_DropsTheWarning_EvenWhenListedAsError()
    {
        var result = Parse(warnAsError: true, asErrors: "MS0001", noWarn: "MS0001")
            .Apply([Warning("MS0001"), Warning("MS0002")]);

        await Assert.That(result.Single().Code).IsEqualTo("MS0002");
    }

    [Test]
    public async Task ExistingErrors_AreKept()
    {
        var error = new MetanoDiagnostic(MetanoDiagnosticSeverity.Error, "MS0001", "text");

        var result = Parse(noWarn: "MS0001").Apply([error]);

        await Assert.That(result.Single().Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    [Test]
    [Arguments("MS0001;MS0002")]
    [Arguments("MS0001,MS0002")]
    [Arguments(" ms0001 \n CS8618;MS0002;nullable;1591")]
    public async Task Lists_AcceptMsBuildSeparatorsAndIgnoreOtherCodes(string value)
    {
        var result = Parse(asErrors: value)
            .Apply([Warning("MS0001"), Warning("MS0002"), Warning("MS0003")]);

        await Assert
            .That(result.Select(d => d.Severity))
            .IsEquivalentTo([
                MetanoDiagnosticSeverity.Error,
                MetanoDiagnosticSeverity.Error,
                MetanoDiagnosticSeverity.Warning,
            ]);
    }
}
