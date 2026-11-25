using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// BCL members the lowering cannot model report MS0001 instead of emitting an undefined
/// property or a missing method: the <c>DateTime</c> kind members (a <c>PlainDateTime</c> has
/// no kind) and the <c>HashCode</c> overloads the runtime accumulator lacks.
/// </summary>
public class UnmodeledBclMemberTests
{
    [Test]
    [Arguments("d.Kind", "'DateTime.Kind' is not supported")]
    [Arguments("d.ToUniversalTime()", "'DateTime.ToUniversalTime' is not supported")]
    [Arguments("d.ToLocalTime()", "'DateTime.ToLocalTime' is not supported")]
    [Arguments(
        "DateTime.SpecifyKind(d, DateTimeKind.Utc)",
        "'DateTime.SpecifyKind' is not supported"
    )]
    public async Task DateTimeKindMembers_ReportMs0001(string expression, string message)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            [Transpile]
            public class Stamps
            {
                public object Run(DateTime d) => {{expression}};
            }
            """
        );

        var reports = diagnostics
            .Where(diagnostic =>
                diagnostic.Code == DiagnosticCodes.UnsupportedFeature
                && diagnostic.Message.Contains(message)
            )
            .ToList();

        await Assert.That(reports.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(
        "hash.Add(a, StringComparer.Ordinal)",
        "string a",
        "'HashCode.Add' with an IEqualityComparer"
    )]
    [Arguments(
        "hash.AddBytes(new byte[] { 1, 2 })",
        "string a",
        "'HashCode.AddBytes' is not supported"
    )]
    public async Task HashCodeOverloads_ReportMs0001(
        string statement,
        string parameter,
        string message
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            [Transpile]
            public class Hashes
            {
                public int Run({{parameter}})
                {
                    var hash = new HashCode();
                    {{statement}};

                    return hash.ToHashCode();
                }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Count(diagnostic =>
                    diagnostic.Code == DiagnosticCodes.UnsupportedFeature
                    && diagnostic.Message.Contains(message)
                )
            )
            .IsEqualTo(1);
    }

    [Test]
    public async Task HashCodeAdd_WithoutAComparerReportsNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Hashes
            {
                public int Run(string a)
                {
                    var hash = new HashCode();
                    hash.Add(a);

                    return hash.ToHashCode();
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }
}
