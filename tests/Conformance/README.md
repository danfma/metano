# Transpile conformance set

A yardstick for "does the transpiled TypeScript print what .NET prints". Each **row** is a
public static `string` method of a `*Probes` class: a few lines of C# that exercise one
behaviour and return a deterministic text. The set records, per row:

- the **.NET 10** output (`recorded/dotnet.txt`),
- the current Metano output under **Bun** (`recorded/bun.txt`) and under **Node**
  (`recorded/node.txt`), stored as the rows that differ from .NET (a row not listed there
  matches .NET).

A change to the compiler or the runtime runs the whole set and must not turn any row into
`CHANGED`. It is the regression gate the wave-2 groups use before every hand-in; the
unit and golden tests (`tests/Metano.Tests`) stay the place for shape-level assertions.

## Run it

```sh
scripts/run-conformance.sh                  # build, transpile, run Bun + Node, compare (about 20 s cold)
scripts/run-conformance.sh --no-build       # reuse the last transpile in tests/Conformance/.work (about 1 s)
scripts/run-conformance.sh --all            # list every row, not only the ones that are not MATCH
scripts/run-conformance.sh --filter Cast    # rows whose name matches a regular expression
scripts/run-conformance.sh --live-dotnet    # compare against a live `dotnet run` of the probes
scripts/run-conformance.sh --record         # re-record .NET, Bun and Node (see "Record")
```

The script builds the TypeScript CLI and `metano-runtime`, transpiles the probes
(`Conformance.csproj`) and the library half of the cross-package row
(`xpkg/Lib/ConformanceLib.csproj`), compiles the output with `tsgo` for Node, then runs every
module of every probe class in its own Bun and Node process, each under a timeout
(`CONFORMANCE_TIMEOUT_MS`, default 30000). Bun runs the generated `.ts` directly; Node runs the
`tsgo` output through the extensionless-specifier hook in `tools/node-extensionless.mjs` (the
same shim as the issue-tracker sample; #573 removes both). The exit code is 1 when a row
`CHANGED`, or when the C# sources changed since the .NET output was recorded.

The probes are **not** part of `Metano.slnx`: `dotnet build Metano.slnx` never compiles them,
`Conformance.csproj` has no `ProjectReference` from any solution project, and the script
builds it only on demand.

## Status legend

| Status | Meaning |
| --- | --- |
| `MATCH` | The runtime prints exactly what .NET prints. |
| `KNOWN GAP (#N)` | It differs from .NET exactly as recorded; `known-gaps.txt` names the follow-up issue. |
| `KNOWN GAP (no issue)` | Same, but no open issue covers the divergence yet (listed as `none` in `known-gaps.txt`). |
| `KNOWN GAP (unlisted)` | It differs as recorded, but `known-gaps.txt` has no line for the row: add one. |
| `CHANGED` | The output differs from the recording: the regression signal (or an unrecorded improvement/worsening). |

When a row now matches .NET where the recording diverged, the report prints a note; delete its
line in `known-gaps.txt` and re-record.

## Add a row

1. Add a `public static string Name()` method to a `*Probes` class in `probes/` (or a new
   `<Area>Probes.cs` class: it is picked up by name). Print only deterministic values, use
   `Console`-free code, and catch exceptions into the text when the exception is the behaviour.
   Types the probes need live in the same file or in `probes/`; a type from another package
   goes into `xpkg/Lib/`.
2. Run `scripts/run-conformance.sh --record`: it runs the probes on .NET 10, writes
   `recorded/dotnet.txt` (with a hash of the C# sources), and records the current Bun and Node
   output. Review the diff of `recorded/`: the .NET line is the truth the row aims at.
3. If Bun or Node differ from .NET, add a line to `known-gaps.txt` with the follow-up issue
   (`#N`), or `none` plus a note when no issue exists, and report the divergence.
4. Re-run the script without `--record`: it must exit 0 with the row as `MATCH` or
   `KNOWN GAP`.

The recorded .NET output is the list of rows. A probe method the generated JavaScript exposes
but .NET does not (a private helper, or a method with optional parameters) is not a row.

## Record

`--record` rewrites all three files from scratch, so review the diff: a row that moved from
`MATCH` to a divergence is a regression you are about to bless. Use `--record-dotnet` after a
probe edit that does not change any behaviour (the hash in `recorded/dotnet.txt` changes with
every `.cs` edit under `tests/Conformance`, including a `dotnet csharpier` reformat).

## Layout

```
tests/Conformance/
  Conformance.csproj        probe project (net10.0, references src/Metano, not in Metano.slnx)
  Runner.cs                 the .NET side: runs every *Probes method by reflection
  probes/*.cs               the rows, one class per area
  xpkg/Lib/                 library half of the cross-package row (package "conformance-lib")
  recorded/                 dotnet.txt, bun.txt, node.txt
  known-gaps.txt            row -> follow-up issue
  tools/                    orchestrator, per-module runner, Node resolve hook, package templates
  .work/                    (git-ignored) transpiled output and tsgo build
scripts/run-conformance.sh  the entry point
```

## Areas

Value equality (`EqualityProbes`, `DispatchProbes`, `CrossPackageProbes`), casts
(`CastProbes`), defaults (`DefaultProbes`), decimal (`DecimalProbes`), strings and text
(`StringProbes`, `TextProbes`, `NullGuardProbes`), parsing and BCL mappings (`ParseProbes`,
`Round3Probes`), indexers and tuples (`IndexProbes`), LINQ (`LinqProbes`, `LinqSetProbes`),
exceptions (`CatchProbes`, `Round2Probes`), static constructors and read-only statics
(`StaticProbes`), aliases (`AliasProbes`), cycles (`CycleProbes`).

## Traceability

The set is tooling, not a feature: it has no FR of its own. It checks, row by row, the
requirement that generated code keeps the C# semantics, and it is the executable companion of:

- spec 001 FR-064 (explicit type arguments), FR-065 (explicit reference downcasts),
  FR-066 (static constructors, #681 amends it) and FR-067 (dispatcher order);
- the spec 012 reference conformance (`tests/Int64Reference`, `tests/BclReference`), which
  compare .NET fixtures against the runtime helpers; this set compares whole transpiled
  programs.
