using System.Collections.Concurrent;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Records the cross-package npm dependencies a run's emitted code requires, keyed by
/// package name and pre-formatted to a version specifier. Every entry lands in the
/// run-wide <see cref="All"/> map; while a file group is being transformed inside
/// <see cref="BeginGroup"/>, the entry is also attributed to that group so the per-group
/// cache (ADR-0023) can persist it and restore it when the group is reused (#299).
/// </summary>
/// <remarks>
/// Thread-safe: the per-group transform loop fans out across worker threads. The group
/// attribution flows through an <see cref="AsyncLocal{T}"/>, so each worker records into
/// the group it is transforming.
/// </remarks>
public sealed class CrossPackageDependencyLedger
{
    private readonly ConcurrentDictionary<string, string> _all = new(StringComparer.Ordinal);
    private readonly AsyncLocal<ConcurrentDictionary<string, string>?> _currentGroup = new();

    /// <summary>Every dependency recorded or restored during the run.</summary>
    public IReadOnlyDictionary<string, string> All => _all;

    public void Record(string packageName, string version)
    {
        _all[packageName] = version;

        if (_currentGroup.Value is { } group)
            group[packageName] = version;
    }

    /// <summary>
    /// Adds the dependencies a reused group recorded on the run that produced it, so the
    /// run-wide map matches what transforming that group would have recorded.
    /// </summary>
    public void Restore(IReadOnlyDictionary<string, string> dependencies)
    {
        foreach (var (packageName, version) in dependencies)
            _all[packageName] = version;
    }

    /// <summary>
    /// Attributes every dependency recorded on the current execution flow to a new group
    /// until the returned recording is disposed.
    /// </summary>
    public GroupRecording BeginGroup()
    {
        var recording = new GroupRecording(this, _currentGroup.Value);
        _currentGroup.Value = recording.Entries;

        return recording;
    }

    /// <summary>The dependencies attributed to one file group.</summary>
    public sealed class GroupRecording : IDisposable
    {
        private readonly CrossPackageDependencyLedger _ledger;
        private readonly ConcurrentDictionary<string, string>? _enclosingGroup;

        internal GroupRecording(
            CrossPackageDependencyLedger ledger,
            ConcurrentDictionary<string, string>? enclosingGroup
        )
        {
            _ledger = ledger;
            _enclosingGroup = enclosingGroup;
        }

        internal ConcurrentDictionary<string, string> Entries { get; } =
            new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string> Dependencies => Entries;

        public void Dispose() => _ledger._currentGroup.Value = _enclosingGroup;
    }
}
