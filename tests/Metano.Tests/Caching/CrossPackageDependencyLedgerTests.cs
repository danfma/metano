using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Tests.Caching;

/// <summary>
/// Issue #299: reused groups restore cached dependencies while transformed groups record
/// fresh ones on other worker threads, so the outcome must not depend on their order.
/// </summary>
public class CrossPackageDependencyLedgerTests
{
    [Test]
    public async Task Restore_AfterRecord_KeepsRecordedVersion()
    {
        var ledger = new CrossPackageDependencyLedger();

        ledger.Record("@acme/shared", "2.0.0");
        ledger.Restore(new Dictionary<string, string> { ["@acme/shared"] = "1.2.3" });

        await Assert.That(ledger.All["@acme/shared"]).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task Record_AfterRestore_ReplacesRestoredVersion()
    {
        var ledger = new CrossPackageDependencyLedger();

        ledger.Restore(new Dictionary<string, string> { ["@acme/shared"] = "1.2.3" });
        ledger.Record("@acme/shared", "2.0.0");

        await Assert.That(ledger.All["@acme/shared"]).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task SnapshotDependencies_ListsGroupEntriesInOrdinalOrder()
    {
        var ledger = new CrossPackageDependencyLedger();
        IReadOnlyDictionary<string, string> snapshot;

        using (var recording = ledger.BeginGroup())
        {
            ledger.Record("zod", "^3.0.0");
            ledger.Record("@acme/shared", "1.2.3");
            ledger.Record("decimal.js", "^10.0.0");
            snapshot = recording.SnapshotDependencies();
        }

        ledger.Record("outside-group", "1.0.0");

        await Assert.That(string.Join(",", snapshot.Keys)).IsEqualTo("@acme/shared,decimal.js,zod");
    }
}
