# Quickstart: BCL collections as runtime types, runtime type descriptors

## Set up a worktree (one per stage)

Each stage of plan.md gets its own worktree and branch from the `release/3.0` integration branch (or `origin/main` until it exists). Example for 011a:

```bash
git fetch origin
```

```bash
git worktree add ../Metano-issue-335-a -b feat/335-runtime-exceptions origin/main
```

```bash
cd ../Metano-issue-335-a && bun install && dotnet tool restore && dotnet husky install
```

```bash
dotnet build Metano.slnx -maxcpucount:1
```

## Reproduce the gaps on `main`

Transpile a scratch project (outside the repo) with:

```csharp
var xs = new List<int> { 1, 2 };
var x = xs[5];                                  // TS: undefined (C#: ArgumentOutOfRangeException)
var s = new Stack<int>(); var top = s.Pop();    // TS: undefined (C#: InvalidOperationException)
try { Run(); } catch (InvalidOperationException e) { }   // TS: catch (e) — catches everything
bool IsList(object o) => o is List<int>;        // MS0001
bool IsA<T>(object o) => o is T;                // MS0001
var n = typeof(int);                            // MS0001
var t = xs.GetType();                           // emitted as an ordinary call; no runtime answer
```

```bash
dotnet run --project src/Metano.Compiler.TypeScript/ -- -p ../scratch/Scratch.csproj -o ../scratch/out --clean
```

Inspect `../scratch/out` and the MS0001 lines in the console.

## Run the suites that this feature touches

```bash
dotnet run --project tests/Metano.Tests/
```

```bash
cd targets/js/metano-runtime && bun run build && bun test
```

```bash
for s in sample-todo sample-issue-tracker sample-todo-service; do (cd targets/js/$s && bun run build && bun test) || exit 1; done
```

## Benchmark (stage 011b, review artifact)

```bash
cd targets/js/metano-runtime && bun run bench
```

Record the table in `docs/runtime-types.md`; every NFR-001 budget must hold before 011b merges.

## Verify the zero-cost invariant (stages 011c / 011d)

```bash
dotnet run --project src/Metano.Compiler.TypeScript/ -- -p samples/SampleTodo/SampleTodo.csproj -o /tmp/before --clean
```

```bash
git checkout <011d-commit> && dotnet run --project src/Metano.Compiler.TypeScript/ -- -p samples/SampleTodo/SampleTodo.csproj -o /tmp/after --clean && diff -r /tmp/before /tmp/after
```

An empty diff is the NFR-002 gate (SampleTodo asks no runtime-type question).

## Before pushing

```bash
find targets/js -name ".metano-stamp" -delete; find targets/js -name ".metano-cache*.json" -delete
```

The pre-push hook runs `scripts/verify-targets-clean.sh`.
