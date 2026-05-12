namespace Metano.Compiler.Watch;

/// <summary>
/// Long-running orchestrator for <c>--watch</c> (#18). Performs an
/// initial pass via the supplied <paramref name="runOnce"/> delegate
/// and then re-runs it every time a relevant file under the project
/// directory changes (debounced 250 ms). The CLI builds the delegate
/// so target-specific post-emit work (e.g., the TypeScript target's
/// <c>PackageJsonWriter</c>) runs once per change as well, and the
/// incremental cache (ADR-0021) absorbs no-op ticks so an editor's
/// save-on-focus-loss costs only the load + extract phases.
///
/// <para>
/// MVP scope:
/// </para>
/// <list type="bullet">
///   <item>One <see cref="FileSystemWatcher"/> rooted at the project's
///   directory, recursive, filtering <c>.cs</c>, <c>.csproj</c>,
///   <c>.props</c>, and <c>.targets</c> files outside <c>bin/</c> and
///   <c>obj/</c>.</item>
///   <item>250 ms quiet-period debounce so an IDE that fires a burst
///   of events on save coalesces into a single recompile.</item>
///   <item><see cref="CancellationToken"/> exit (the CLI wires it to
///   <see cref="Console.CancelKeyPress"/>).</item>
///   <item>Any exception in the delegate is logged with a full stack
///   trace but does not stop the watcher — it returns to the wait
///   loop so a transient compile failure does not kill the session.</item>
/// </list>
///
/// <para>
/// Reference assembly changes (a sibling project rebuilding) are not
/// observed by the watcher; they are picked up on the next manual save
/// because the incremental cache's reference fingerprint detects them.
/// </para>
/// </summary>
public static class WatchHost
{
    private const int DebounceMs = 250;

    public static async Task<int> RunAsync(
        string projectPath,
        Func<Task> runOnce,
        CancellationToken cancellationToken
    )
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var watchDir =
            Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException(
                $"Cannot derive watch directory from project path '{fullProjectPath}'."
            );

        Console.WriteLine(
            $"Metano: watching {watchDir} for .cs / .csproj / .props / .targets changes …"
        );
        Console.WriteLine("  (Ctrl+C to exit)");

        await SafeRun(runOnce);

        using var watcher = CreateWatcher(watchDir);
        var changeSignal = new SemaphoreSlim(0, int.MaxValue);
        // The watcher fires events on background threads; the wait loop
        // reads the timestamp on the consumer thread. Store ticks in a
        // long behind Volatile.Read/Write so the debounce always sees
        // the latest value and never tears on 32-bit.
        var lastEventAtUtcTicks = DateTime.UtcNow.Ticks;

        void OnFileEvent(string path)
        {
            if (!IsRelevant(path))
                return;
            Volatile.Write(ref lastEventAtUtcTicks, DateTime.UtcNow.Ticks);
            changeSignal.Release();
        }

        watcher.Changed += (_, e) => OnFileEvent(e.FullPath);
        watcher.Created += (_, e) => OnFileEvent(e.FullPath);
        watcher.Deleted += (_, e) => OnFileEvent(e.FullPath);
        // Rename inspects BOTH paths so a rename that strips a relevant
        // extension (Foo.cs → Foo.txt) or moves the file under bin/obj
        // still triggers a recompile.
        watcher.Renamed += (_, e) =>
        {
            OnFileEvent(e.OldFullPath);
            OnFileEvent(e.FullPath);
        };
        // FileSystemWatcher's internal buffer can overflow under a high
        // volume of events, dropping unobserved changes. Surface the
        // problem to the user instead of silently missing recompiles —
        // the next manual save will catch up via the cache fingerprint.
        watcher.Error += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine(
                $"  Metano: file watcher dropped events ({e.GetException().Message}). "
                    + "Save once more to force a recompile."
            );
            Console.ResetColor();
        };
        watcher.EnableRaisingEvents = true;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await changeSignal.WaitAsync(cancellationToken);
                await WaitForQuietPeriod(
                    () => new DateTime(Volatile.Read(ref lastEventAtUtcTicks), DateTimeKind.Utc),
                    cancellationToken
                );
                Drain(changeSignal);

                Console.WriteLine();
                Console.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss}] Metano: change detected, recompiling …"
                );
                await SafeRun(runOnce);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C — fall through to the clean shutdown line.
        }

        Console.WriteLine("Metano: watch mode stopped.");
        return 0;
    }

    private static FileSystemWatcher CreateWatcher(string watchDir) =>
        new(watchDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.CreationTime
                | NotifyFilters.Size,
        };

    /// <summary>
    /// Sleeps just long enough for the burst to settle. Always delays
    /// for the *remaining* quiet-period (DebounceMs minus elapsed
    /// since the last event) instead of a fixed slice — without that
    /// adjustment the effective debounce drifts up to nearly 2×
    /// <see cref="DebounceMs"/> when an event lands right after a
    /// delay starts.
    /// </summary>
    private static async Task WaitForQuietPeriod(
        Func<DateTime> lastEventAtUtc,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var elapsed = (DateTime.UtcNow - lastEventAtUtc()).TotalMilliseconds;
            var remaining = DebounceMs - elapsed;
            if (remaining <= 0)
                return;
            await Task.Delay((int)Math.Ceiling(remaining), cancellationToken);
        }
    }

    private static void Drain(SemaphoreSlim signal)
    {
        while (signal.Wait(0))
        {
            // Discard every queued release — the recompile we are
            // about to start covers them all.
        }
    }

    /// <summary>
    /// Runs <paramref name="runOnce"/>, swallowing every exception so
    /// the watcher survives a transient compile failure or filesystem
    /// hiccup. The exception is logged with the full stack trace so a
    /// real bug stays debuggable from the watch console.
    /// </summary>
    private static async Task SafeRun(Func<Task> runOnce)
    {
        try
        {
            await runOnce();
        }
        catch (Exception ex)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"  Metano: unexpected exception:\n{ex}");
            }
            finally
            {
                Console.ResetColor();
            }
        }
    }

    /// <summary>
    /// Filter the file-system events down to the artifacts that drive
    /// re-compilation. .cs sources, the .csproj itself, and
    /// project-level MSBuild props/targets are the inputs the cache
    /// fingerprint already covers; everything under <c>bin/</c> or
    /// <c>obj/</c> (including .cs source-generator output and
    /// MSBuild-generated .csproj caches) is build-byproduct noise.
    /// </summary>
    internal static bool IsRelevant(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        if (ContainsBuildArtifactSegment(path))
            return false;

        var ext = Path.GetExtension(path);
        if (ext.Length == 0)
            return false;
        return ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".props", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".targets", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Allocation-free scan: <see cref="FileSystemWatcher"/> is chatty
    /// enough that <see cref="string.Split(char[])"/> per event would
    /// dominate the GC profile. <see cref="MemoryExtensions"/> tokenises
    /// the path into separator-bounded slices and we compare each slice
    /// against the literal directory names without materialising a
    /// <see cref="string"/>.
    /// </summary>
    private static bool ContainsBuildArtifactSegment(string path)
    {
        var span = path.AsSpan();
        foreach (var range in span.SplitAny('/', '\\'))
        {
            var segment = span[range];
            if (
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            )
                return true;
        }
        return false;
    }
}
