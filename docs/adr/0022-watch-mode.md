# ADR-0022 — `--watch` mode

**Status:** Accepted
**Date:** 2026-05-07

## Context

Issue #18 asks for a long-running transpiler that re-emits whenever
the source project changes, so iterative development does not pay the
"open MSBuild project + create Roslyn compilation" cost on every save.
The two prerequisites — fast incremental rebuilds (#21 / ADR-0021)
and a parallel transformer (#21 / ADR-0020) — are now in place: the
incremental cache absorbs no-op ticks (most editor saves) at the cost
of just the load + extract phases, and a real change runs the
transform across worker threads.

## Decision

Add `--watch` to both `metano-typescript` and `metano-dart`, backed
by a target-agnostic `WatchHost` orchestrator under
`src/Metano.Compiler/Watch/`.

```text
WatchHost.RunAsync(projectPath, runOnce, ct)
```

- `runOnce` is a delegate the CLI builds; it captures the same
  pipeline the non-watch path runs (initial transpile + any
  target-specific post-emit such as the TypeScript target's
  `PackageJsonWriter`). This keeps `WatchHost` ignorant of target
  shape and lets the watcher continue to drive every tick through
  the existing CLI plumbing.
- A single `FileSystemWatcher` is rooted at the project directory,
  recursive, with `NotifyFilters.LastWrite | FileName | CreationTime
  | Size`. The handler filters down to `.cs`, `.csproj`, `.props`,
  and `.targets` extensions — everything else is editor noise (lock
  files, swap files, `.tsbuildinfo`, `obj/`).
- Events feed a `SemaphoreSlim` triggered after every relevant
  change. The wait loop runs a 250 ms quiet-period debounce so an
  IDE that fires a burst of events on save coalesces into a single
  recompile, and then drains the semaphore so the next tick covers
  the next burst.
- `CancellationToken` exit: the CLI wires `Console.CancelKeyPress` to
  a `CancellationTokenSource` so Ctrl+C triggers a clean shutdown.
- A failed compile (or any exception in the delegate) is logged but
  does not stop the watcher — it returns to the wait loop. Watch
  mode is for iteration; a transient compile failure should not kill
  the session.

## Consequences

(+) Combined with ADR-0021's cache, a `--watch` tick that observes a
   no-op save costs only the load + extract phases (compilation
   already comes back warm; the cache short-circuit covers the
   rest).
(+) A real source edit pays the parallel transform from ADR-0020,
   not a sequential one — `--watch` benefits compound across the
   three PRs in the #21 + #18 thread.
(+) Target-agnostic: the Dart CLI gets the same loop with one extra
   line.
(+) No new dependency. `FileSystemWatcher` is in the BCL.
(−) Reference assembly changes (a sibling project rebuilding) do
   not trigger a recompile. They are still picked up on the next
   manual save because the cache's reference fingerprint flips, but
   the user has to poke the project. Tracked as a follow-up if
   real-world use shows the gap.
(−) The watcher reacts to events in the project's *source*
   directory but not to the consumer-side `package.json` (the
   TypeScript target writes it from outside the project tree).
   Acceptable today: the package.json regeneration is idempotent and
   only fires on the active recompile.
(−) `FileSystemWatcher` cross-platform burst behavior is absorbed
   by the debounce + drain. Manual smoke tested on macOS.
(−) No automated test for the watch loop itself — `FileSystemWatcher`
   semantics are too platform-dependent for a reliable unit test.
   `WatchHost.IsRelevant` is a pure static and has direct test
   coverage; tests for the underlying primitives
   (`TranspilerHost`, cache, parallel transform) cover the
   load-bearing pieces.

## Alternatives considered

- **Inline the watch loop in each target's CLI.** Rejected: every
  target would re-implement the same debounce + filter + cancel
  glue, drifting in details. Centralising in `WatchHost` keeps the
  semantics aligned across `metano-typescript` and `metano-dart`.
- **Spawn a child process per tick.** Rejected: defeats the warm
  Roslyn workspace and the in-memory incremental cache state.
  Process-per-tick would regress to today's full cold start.
- **Watch the metadata references (the .dll set Roslyn discovered).**
  Rejected for MVP: requires walking the compilation's references
  and registering watchers per directory, plus filtering to project
  `.dll` outputs vs BCL noise. The "manual save after sibling
  rebuild" workflow is an acceptable fallback while the feature is
  young.
- **Use `dotnet watch` as the orchestrator.** Rejected: ties the
  feature to the .NET SDK runner, complicates the Dart target's
  story, and forces every consumer through the SDK's reload
  semantics instead of Metano's pipeline.

## References

- `src/Metano.Compiler/Watch/WatchHost.cs`
- `src/Metano.Compiler.TypeScript/Commands.cs` (`--watch` plumbing
  + `RunOnce` delegate)
- `src/Metano.Compiler.Dart/Commands.cs` (`--watch` plumbing)
- ADR-0020 — parallel TypeTransformer (the transform that runs each
  tick is the parallel one)
- ADR-0021 — incremental cache (the no-op tick path)
- Issue #18 — watch mode
- Issue #21 — incremental compilation + parallel TypeTransformer
