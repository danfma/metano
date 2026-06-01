# Quickstart: JSX/TSX from C#

**Feature**: `002-jsx-codegen-from-csharp` | **Date**: 2026-06-01

How to build, transpile, and validate the JSX slice once implemented. Commands run from the repo root unless noted.

## Prerequisites

- .NET 10 SDK (`global.json` pins it).
- Bun (never npm/yarn/pnpm).
- The SolidJS + DOM bindings already in the solution: `bindings/Metano.TypeScript.SolidJs`, `bindings/Metano.TypeScript.DOM`.

## 1. Build the transpiler + run .NET golden tests

```sh
dotnet build                                  # builds the whole solution
dotnet run --project tests/Metano.Tests/      # TUnit (use run, not test)
```

The new JSX golden tests live in `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` with expected output in `tests/Metano.Tests/Expected/*.tsx`. To (re)generate an expected file while iterating, transpile inline and inspect `result["…tsx"]` — then write the verified output to `Expected/`.

Adding a golden test (pattern):
```csharp
[Test]
public async Task Counter_LowersToFunctionComponent()
{
    var result = TranspileHelper.Transpile(
        """
        using Metano.Annotations;
        using Metano.TypeScript.SolidJs;
        using Metano.TypeScript.SolidJs.Web;

        [Transpile]
        public sealed record Counter : JsxComponent {
            public int Count { get; init; }
            public override JsxElement Render() =>
                new Html.Span { Children = [Text(Count)] };
        }
        """);
    var expected = TranspileHelper.ReadExpected("counter-span.tsx");
    await Assert.That(result["counter.tsx"]).IsEqualTo(expected);
}
```
Cross-package / imported-renderable test (SC-004) uses `TranspileHelper.TranspileWithLibrary(librarySource, consumerSource)`.

## 2. Transpile the sample end-to-end

`SampleSolidUi.csproj` is wired (this feature) with `MetanoOutputDir` + the `Metano.Build` MSBuild import, mirroring `SampleCounterV1`. So a plain build transpiles it:

```sh
dotnet build samples/SampleSolidUi/            # auto-runs MetanoTranspile AfterBuild → targets/js/sample-solid-ui/src
```

Manual CLI form (equivalent):
```sh
dotnet run --project src/Metano.Compiler.TypeScript/ -- \
  -p samples/SampleSolidUi/SampleSolidUi.csproj \
  -o targets/js/sample-solid-ui/src --clean
```

Expected: `.tsx` files under `targets/js/sample-solid-ui/src/ui/` (`counter.tsx`, `counter-group.tsx`) and `program.tsx`, with SolidJS imports (`createSignal`, `For`, `render`).

## 3. Build & test the SolidJS consumer

The consumer (`targets/js/sample-solid-ui/`) mirrors `sample-counter-v1`: Vite + `vite-plugin-solid`, `tsconfig` with `jsx: "preserve"`, `jsxImportSource: "solid-js"`, registered in the root Bun workspace.

```sh
cd targets/js/sample-solid-ui && bun install   # first time
cd targets/js/sample-solid-ui && bun run build  # tsgo + vite build of generated .tsx
cd targets/js/sample-solid-ui && bun test       # bun:test end-to-end
cd targets/js/sample-solid-ui && bun run dev     # local dev server (manual visual check)
```

**Acceptance (SC-001, SC-005)**: `bun run build` succeeds with **zero manual edits** to generated `.tsx`, and the counter group renders/increments in the browser.

## 4. What "done" looks like (traceability)

| Check | Source |
|-------|--------|
| `Counter`/`CounterGroup` → function components, props types, signal bodies | SC-001, SC-002, C1–C4 |
| All reactivity bindings lower to documented Solid forms (golden) | SC-003, R1–R6 |
| Recognition works for native HTML **and** one imported type | SC-004, R7 |
| Generated consumer builds & renders | SC-005 |
| Unrecognized renderable → `MS0026`, not silent wrong output | SC-006, diagnostics contract |

## 5. Review gate before commit (Constitution + CLAUDE.md)

Run `compiler-man` (semantic/AST/lowering correctness) and `bob` (Clean Code) in parallel on the diff, fix findings, then `dotnet csharpier .`, then commit on this branch with a conventional-commit message. Update the baseline capability matrix entry for the new UI-component capability.
