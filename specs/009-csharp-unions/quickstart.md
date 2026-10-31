# Quickstart: Validating C# 15 union support

Run every step from the repo root. Each stage (plan.md) has its own section, and a stage passes only when its checks pass.

## Prerequisites

```sh
dotnet --list-sdks          # needs 10.0.x and the exact 11 RC pinned in global.json (11.0.100-rc.1.26425.128 after 009a)
bun install && dotnet tool restore && dotnet husky install
dotnet build Metano.slnx -maxcpucount:1
```

## 009a — Toolchain

1. `dotnet run --project tests/Metano.Tests/ --no-build`. **Expect**: the same total, passed and skipped counts as `main` (the 7 `ExtensionIndexerTests` stay skipped).
2. `git diff --exit-code targets/`. **Expect**: no change.
3. `dotnet csharpier check .`. **Expect**: it passes. If it fails on union syntax once 009b adds union sources, add them to `.csharpierignore` (risk K3).
4. Run the tool on a machine that has only .NET 11:
   ```sh
   dotnet pack src/Metano.Compiler.TypeScript -o /tmp/feed
   DOTNET_ROOT=<dir with only SDK 11 + runtime 11> dotnet tool install Metano.Compiler.TypeScript \
     --add-source /tmp/feed --tool-path /tmp/tool --prerelease
   /tmp/tool/metano-typescript --help
   ```
   **Expect**: the tool starts with no "You must install or update .NET" error (FR-002).

## 009b — Shape

Union tests compile against the net11 reference pack resolved from `global.json` (FR-036). If the pack is missing, the test run fails with a message naming the expected `packs/Microsoft.NETCore.App.Ref/<version>/ref/net11.0` path; install the pinned SDK to fix it.

```sh
dotnet run --project src/Metano.Compiler.TypeScript/ -- \
  -p samples/SampleUnions/SampleUnions.csproj -o targets/js/sample-unions/src --clean
```

**Expect** `clip.ts` to contain `export type Clip = ImageClip | SilenceClip;`, with no `class Clip` and no `value`. `Make()` should return `new SilenceClip(2)`, and `Wrap(i)` should return `i`.

## 009c — Patterns

```sh
cd targets/js/sample-unions && bun run build
```

**Expect**: tsgo strict passes. Delete one `if ($s instanceof …)` block from `describe` by hand and rebuild. **Expect** `TS1360: Type 'SilenceClip' does not satisfy the expected type 'never'.` Then revert the edit.

## 009d — JSON

```sh
dotnet run --project samples/SampleUnions.Fixtures/    # regenerate fixtures from real .NET 11 STJ (OD-2a)
git diff --exit-code targets/js/sample-unions/test/fixtures
cd targets/js/sample-unions && bun test
```

**Expect**: every fixture round-trips byte-for-byte, and every error fixture throws with the STJ message prefix. The transpile run prints `warning MS0036 … 'Clip' … 'Object'`.

## Regression gate

```sh
dotnet run --project tests/Metano.Tests/ --no-build
cd targets/js/metano-runtime && bun run build && bun test
cd targets/js/sample-todo && bun run build && bun test
cd targets/js/sample-todo-service && bun run build && bun test
cd targets/js/sample-issue-tracker && bun run build && bun test
dotnet csharpier check .
```
