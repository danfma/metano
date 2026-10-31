# Quickstart: System.Text.Json-faithful JSON contracts

## Set up a worktree

```bash
git fetch origin
```

```bash
git worktree add ../Metano-issue-268 -b feat/268-polymorphic-json origin/main
```

```bash
cd ../Metano-issue-268 && bun install && dotnet tool restore && dotnet husky install
```

```bash
dotnet build Metano.slnx -maxcpucount:1
```

## Reproduce the gaps on `main`

Create a scratch project (outside the repo) with the `Clip` hierarchy, a
`[JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]` root and a `[Branded] Title` with a
validating `From` (see [contracts/context-codegen.md](./contracts/context-codegen.md)), then:

```bash
dotnet run --project src/Metano.Compiler.TypeScript/ -- -p <scratch>/Scratch.csproj -o <scratch>/out --clean
```

Expected today: `clip` has no discriminator handling, `imageClip` is missing unless listed,
`TypeSpec<IReadOnlyList>` with `new IReadOnlyList()`, no getter for `EpisodeSummary[]`, and
`create: Title.create`.

## Verify after implementation

```bash
dotnet run --project tests/Metano.Tests/ --no-build
```

Expected: the `Json/` tests pass, including `StjConformanceFixtureTests` (STJ side of the fixtures).

```bash
cd targets/js/metano-runtime && bun run build && bun test
```

```bash
cd targets/js/sample-json-contracts && bun run build && bun test
```

Expected: every fixture round-trips byte-for-byte; every error fixture throws the recorded class and
message prefix.

## Regenerate the STJ fixtures (deliberate, local only)

```bash
METANO_UPDATE_STJ_FIXTURES=1 dotnet run --project tests/Metano.Tests/ --no-build -- --treenode-filter "/*/*/StjConformanceFixtureTests/*"
```

Review the fixture diff like any other golden change.

## Existing suites (regression)

```bash
cd targets/js/sample-todo && bun run build && bun test
```

```bash
cd targets/js/sample-todo-service && bun run build && bun test
```

```bash
cd targets/js/sample-issue-tracker && bun run build && bun test
```

```bash
dotnet csharpier check . && bunx biome check .
```
