# SampleIssueTracker

The most **complex and realistic** sample in this repository. A miniature issue
tracking domain model covering multiple bounded contexts, rich business logic,
inheritance, branded IDs, repositories, services, page results, and LINQ queries.

This is Metano's **stress test** — if the transpiler produces correct, idiomatic
TypeScript for this sample, it can handle most real-world C# codebases.

## What this sample demonstrates

### Domain-Driven Design structure

The project is organized into three bounded contexts, each with its own namespace:

```
SampleIssueTracker/
├── Issues/
│   ├── Domain/           # Issue, Comment, IssueWorkflow, status enums
│   └── Application/      # Repository interface + in-memory impl, service, queries
├── Planning/
│   └── Domain/           # Sprint aggregate
└── SharedKernel/         # UserId, IssueId (branded), OperationResult, PageRequest, PageResult
```

This maps naturally to the transpiler's namespace-first import model — each
namespace becomes a directory with a barrel `index.ts`.

### Branded IDs via `[InlineWrapper]`

```csharp
[InlineWrapper]
public readonly record struct UserId(string Value)
{
    public static UserId New() => new(Guid.NewGuid().ToString("N"));
    public static UserId System() => new("system");
    public override string ToString() => Value;
}
```

`UserId` and `IssueId` are zero-cost branded types at the TypeScript level — they're
`string` under the hood, but the type system prevents mixing them up.

### Rich aggregates with encapsulation

`Issue` is a full DDD aggregate:

- **Constructor-initialized state** with sensible defaults
- **Private setters** (`public IssueStatus Status { get; private set; }`)
- **Business methods** that mutate state and emit events (`Rename`, `AssignTo`,
  `TransitionTo`)
- **Invariants enforced in methods** (`TransitionTo` throws when the target status
  is unreachable from the current one)
- **Nullable state** (`AssigneeId`, `SprintKey`) with `| null` in TS
- **Computed properties** (`IsClosed`, `CommentCount`, `Lane`)
- **`private readonly List<Comment> _comments`** exposed as `IReadOnlyList<Comment>`
- **Method overloads** — `TransitionTo(nextStatus, actorId)` AND
  `TransitionTo(nextStatus, actorId, changedAt)`
- **Exception throwing** (`InvalidOperationException`) → `throw new Error(...)` in TS

### Generic types with constraints

`OperationResult<T>` is a generic Result/Either type with static factory methods,
and `PageResult<T>` wraps a list with pagination metadata.

### LINQ queries on real data

`IssueQueries` exposes a module of functions (via `[ExportedAsModule]`) that run
LINQ operations over an `IEnumerable<Issue>`:

```csharp
[ExportedAsModule]
public static class IssueQueries
{
    public static IEnumerable<Issue> HighestPriority(IEnumerable<Issue> issues) =>
        issues.Where(i => i.Priority == IssuePriority.Critical).OrderBy(i => i.CreatedAt);

    public static IReadOnlyDictionary<IssueStatus, int> CountByStatus(IEnumerable<Issue> issues) =>
        issues.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count());
    // ...
}
```

Each of those lowers to a lazy LINQ chain from `metano-runtime`:

```typescript
export function highestPriority(issues: Iterable<Issue>): Iterable<Issue> {
  return Enumerable.from(issues)
    .where((i) => i.priority === IssuePriority.Critical)
    .orderBy((i) => i.createdAt);
}
```

### Workflow with switch expressions and pattern matching

`IssueWorkflow` is a static class with state-machine logic:

```csharp
public static bool CanTransition(IssueStatus from, IssueStatus to) => (from, to) switch
{
    (IssueStatus.Backlog, IssueStatus.Todo) => true,
    (IssueStatus.Todo, IssueStatus.InProgress) => true,
    (IssueStatus.InProgress, IssueStatus.InReview) => true,
    // ...
    _ => false,
};
```

The tuple pattern matching compiles down to `switch`-like lowered code in TS.

### In-memory repository

`InMemoryIssueRepository` implements `IIssueRepository` with a `List<Issue>`,
exposing:

- `Save(issue)` / `Remove(id)`
- `Get(id)` → `Issue?`
- `List()` → `IReadOnlyList<Issue>`
- Pagination via `PageResult<T>` and `PageRequest`

## What gets transpiled

20+ generated TypeScript files, organized by namespace:

```
js/sample-issue-tracker/src/
├── issues/
│   ├── domain/
│   │   ├── issue.ts          # Issue class with all methods
│   │   ├── issue-workflow.ts # State machine
│   │   ├── issue-id.ts       # Branded ID
│   │   ├── issue-status.ts   # String enum
│   │   ├── issue-type.ts     # String enum
│   │   ├── issue-priority.ts # String enum
│   │   ├── comment.ts        # Value object
│   │   └── index.ts          # Barrel
│   └── application/
│       ├── i-issue-repository.ts
│       ├── in-memory-issue-repository.ts
│       ├── issue-service.ts
│       ├── issue-queries.ts   # ExportedAsModule → top-level functions
│       └── index.ts
├── planning/
│   └── domain/
│       └── sprint.ts
├── shared-kernel/
│   ├── user-id.ts             # Branded
│   ├── issue-id.ts (duplicate? no — different ns)
│   ├── operation-result.ts    # Generic Result type
│   ├── page-request.ts
│   ├── page-result.ts
│   └── index.ts
└── index.ts
```

## How to build

```bash
dotnet build samples/SampleIssueTracker/SampleIssueTracker.csproj
```

## How to test

```bash
cd js/sample-issue-tracker
bun install
bun run build
bun test
```

You should see **51 passing tests** that cover:

- `Issue` lifecycle: create, rename, describe, assign, unassign, plan/unplan for
  sprint, add comments
- Status transitions with valid AND invalid paths (expect `Error` thrown)
- `IssueQueries` — `highestPriority`, `countByStatus`, `assignedTo`, etc.
- `InMemoryIssueRepository` CRUD operations
- `PageResult<T>` pagination calculations
- `OperationResult<T>` success/failure branching
- String enum equality checks
- Branded type roundtrips (`UserId.New()`, `UserId.System()`)

## Known issue

There's a cyclic import between `Issue` and `IssueWorkflow`: `issue.ts` imports
`IssueWorkflow` as a value (it instantiates a workflow instance), and
`issue-workflow.ts` imports `Issue` as a type (the workflow methods take an
`Issue` parameter). Metano emits an `MS0005` warning during the build, but the
generated code compiles and runs correctly — TypeScript erases type-only imports
at runtime, so the cycle is harmless in practice.

The current `CyclicReferenceDetector` is deliberately conservative: it treats
every local edge (`#`, `#/...`, `./...`) as load-bearing, regardless of whether
it's a type-only import. See
[ADR-0006](../../docs/adr/0006-namespace-first-barrel-imports.md) for the
namespace-first import strategy that shapes the detector's view of the graph.
A refinement to ignore type-only edges is a natural follow-up; if it becomes
load-bearing for a consumer, open an issue on the tracker.

## Why this matters

This sample is the benchmark for Metano's **coverage of real C# idioms**:

- If a feature works here, it works in real projects
- If a bug shows up in SampleIssueTracker's generated TS, it's a regression test
- The 51 bun tests verify end-to-end behavior, not just "does it compile"

It's also the easiest way to see what a non-trivial Metano project output looks
like. Open [`targets/js/sample-issue-tracker/src/issues/domain/issue.ts`](../../targets/js/sample-issue-tracker/src/issues/domain/issue.ts)
to see the real output.
