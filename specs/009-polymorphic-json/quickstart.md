# Quickstart: System.Text.Json-faithful JSON contracts

## Set up a worktree (one per implementation slice)

Each slice of plan.md ("Implementation slices") gets its own worktree and branch from
`origin/main`. Example for S1 (issue #268):

```bash
git fetch origin
```

```bash
git worktree add ../Metano-issue-268-s1 -b fix/268-json-declaring-assembly origin/main
```

```bash
cd ../Metano-issue-268-s1 && bun install && dotnet tool restore && dotnet husky install
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
`TypeSpec<IReadOnlyList>` with `new IReadOnlyList()`, no getter for `EpisodeSummary[]`, a
`ref → () => this.page` with no `page` getter for a `Page<EpisodeSummary>` property, and
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

Expected: every `ts` row of `test/fixtures/stj/cases.json` passes per its direction (byte equality
for write / roundtrip, `instanceof` for read / roundtrip); every error row throws the recorded class
and message prefix.

## Regenerate the STJ fixtures (deliberate, local only)

```bash
METANO_UPDATE_STJ_FIXTURES=1 dotnet run --project tests/Metano.Tests/ --no-build -- --treenode-filter "/*/*/StjConformanceFixtureTests/*"
```

Review the `cases.json` diff like any other golden change; a row whose STJ text changes but whose
TS text cannot follow becomes `divergent` only with a matching entry in spec.md "Documented
divergences".

## Migrate call sites for the breaking release (S3b)

After S3b, `deserialize` through a `TypeSpec` returns `T | null` (spec.md "Migration"). Build
every consumer and fix each new strict-mode error by handling `null` or asserting it:

```bash
cd targets/js/sample-todo && bun run build
```

```ts
const item = JsonSerializer.deserialize(data, JsonContext.default.todoItem);

if (item === null) {
  throw new Error("expected a todo item");
}
```

The S3b commit uses `feat!:` with a `BREAKING CHANGE:` footer that carries the migration note, so
semantic-release cuts a major version.

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
