# Quickstart: JS-Interop Primitives

**Feature**: `003-js-interop-primitives` | **Date**: 2026-06-01

How to build and validate the primitives. Commands run from the repo root.

## Prerequisites
- .NET 10 SDK. Bun (only if exercising a consumer; not required for the .NET golden tests).
- This feature is self-contained — it does NOT require the branch-002 SolidJS binding.

## 1. Build + run .NET golden tests

```sh
dotnet build Metano.slnx
dotnet run --project tests/Metano.Tests/      # TUnit — use run, not test
```

New golden tests:
- `tests/Metano.Tests/JsTupleTranspileTests.cs` — contracts T1–T5
- `tests/Metano.Tests/JsCallableTranspileTests.cs` — contracts C1–C5
- `tests/Metano.Tests/DeconstructionTranspileTests.cs` — contracts D1–D4
- `tests/Metano.Tests/SignalCompositionTests.cs` — contract S1 (inline `Signal<T>` binding)

Pattern (inline-compiled C#, no external binding needed):
```csharp
[Test]
public async Task JsCallable_OverloadedInvoke_LowersToDirectCall()
{
    var result = TranspileHelper.Transpile(
        """
        using Metano.Annotations;
        using Metano.Annotations.TypeScript;

        [JsCallable, External] public interface ISetter<T> {
            void Invoke(T value);
            void Invoke(System.Func<T, T> updater);
        }

        [ExportedAsModule] public static class Demo {
            public static void Run(ISetter<int> s) { s.Invoke(5); s.Invoke(c => c + 1); }
        }
        """);
    await Assert.That(result["demo.ts"]).IsEqualTo(TranspileHelper.ReadExpected("jscallable-overload.ts"));
}
```

The `Metano.Annotations.TypeScript` attributes (`[JsTuple]`, `[JsCallable]`, `[External]`) are available because the harness references `Metano.Annotations`.

## 2. Diagnostics tests

Use `TranspileHelper.TranspileWithDiagnostics(...)` and assert `MS0027` / `MS0028` for the misuse cases in the diagnostics contract.

## 3. "Done" checks (traceability)

| Check | Source |
|-------|--------|
| `[JsCallable]` `Invoke` (incl. overloads) → `recv(args)`, no `[Emit]` | SC-001, C1–C3 |
| `[JsTuple]` → tuple type alias / erased, no class/equals/hashCode/with, `[i]` access | SC-002, T1–T4 |
| `var (a,b)=e` → `const [a,b]=e` (+ discards) | C-D1–D3 |
| `Signal<T>` composition → idiomatic destructured signal, zero `[Emit]` | SC-003, S1 |
| Marker misuse → MS0027/MS0028, not silent | SC-004, diagnostics |
| Existing golden tests unchanged | SC-005 |

## 4. Review gate before commit (Constitution + CLAUDE.md)

Run `compiler-man` (semantics) and `bob` (Clean Code) in parallel on the diff, fix findings, `dotnet csharpier format .`, then commit on `003-js-interop-primitives`. Add baseline capability-matrix + attribute-catalog + diagnostic-catalog entries for the two attributes and MS0027/MS0028.

## 5. Downstream (separate, after merge)

After `003` merges, the **002 reactivity refactor** rewrites the real `bindings/Metano.TypeScript.SolidJs` (`ISignal.Value/.Set` → `Signal<T>` deconstruction) on top of these primitives and revalidates the SolidJS consumer (`targets/js/sample-solid-ui` + `sample-counter-v5`). That is NOT part of this feature.
