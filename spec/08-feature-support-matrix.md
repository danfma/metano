# Feature Support Matrix

This matrix summarizes the current product support surface at a feature level.
Statuses are intentionally high-level:

- **Implemented**: present in the codebase and part of the current product.
- **Partial**: supported with explicit constraints or non-total language
  coverage.
- **Planned/Out of scope**: not a current product guarantee.

## Language and Semantic Features

| Area | Feature | Status | Notes |
| --- | --- | --- | --- |
| Type selection | `[Transpile]`, `[TranspileAssembly]`, `[NoTranspile]`, `[NoEmit]` | Implemented | Explicit opt-in/out model. |
| Types | Records | Implemented | Includes structural helpers in emitted output. |
| Types | Classes and inheritance | Partial | Supported within the current transpilation surface. |
| Types | Interfaces and generics | Partial | Supported for the current mapped subset. |
| Types | Nullable reference/value types | Implemented | Lowered to TS null unions. |
| Enums | Numeric enums | Implemented | |
| Enums | String enums | Implemented | Via `[StringEnum]`. |
| Async | `Task` / `ValueTask` | Implemented | Lowered to `Promise`. |
| Exceptions | Exception types and throw flows | Partial | Supported within the current transformation rules. |
| Pattern matching | `switch`, `is`, supported patterns | Partial | Depends on current handler support. |
| Expressions | Lambdas and interpolated strings | Implemented | |
| Expressions | Collection expressions (`[]`) | Implemented | Covered by dedicated lowering. |
| Modules | `[ExportedAsModule]` | Implemented | Static class -> module functions. |
| Modules | `[ModuleEntryPoint]` | Implemented | Lowers method body to top-level statements. |
| Modules | C# top-level statements | Implemented | Lowered to module-level code. |

## Operators and Overloads

| Area | Feature | Status | Notes |
| --- | --- | --- | --- |
| Operators | User-defined binary operators | Partial | Supported when lowered by current operator rules. |
| Operators | User-defined unary operators | Partial | Supported when explicitly mapped/named as required by current output rules. |
| Operators | Prefix/postfix increment and decrement | Implemented | Preserves JS-compatible evaluation order. |
| Operators | Type-test and comparison lowerings | Partial | Supported within the current transformation surface. |
| Overloads | Method overload dispatch | Implemented | Dispatcher + specialized fast-path methods. |
| Overloads | Constructor overload dispatch | Implemented | Same strategy as methods. |

## Collections, LINQ, and Runtime Surface

| Area | Feature | Status | Notes |
| --- | --- | --- | --- |
| Collections | `List<T>` / arrays | Implemented | |
| Collections | `Dictionary<K,V>` / `Map<K,V>` | Implemented | |
| Collections | `HashSet<T>` | Implemented | Requires runtime support. |
| Collections | `Queue<T>` / `Stack<T>` | Implemented | |
| Collections | `ImmutableList<T>` | Implemented | Lowered through immutable collection mappings. |
| Collections | `ImmutableArray<T>` | Implemented | Lowered through immutable collection mappings. |
| LINQ | Core LINQ runtime layer | Implemented | Product-defined subset. |
| Delegates | `Action`/delegate type lowering | Implemented | Emits function types. |
| Events | Event fields with add/remove semantics | Implemented | Uses `delegateAdd` / `delegateRemove`. |

## Output, Packaging, and Diagnostics

| Area | Feature | Status | Notes |
| --- | --- | --- | --- |
| Output | Namespace-based imports and barrels | Implemented | Guided by ADR-0006 and ADR-0007. |
| Packaging | `[EmitPackage]` cross-package support | Implemented | Includes `package.json` dependency propagation. |
| Packaging | `Metano` and `Metano.Build` as build-only consumer dependencies | Planned/Partial | Intended to avoid unnecessary runtime/transitive contribution in consuming .NET projects. |
| Packaging | Subdirectory-aware `package.json` imports/exports (`--src-root`) | Planned | When output targets a subdirectory of the source tree, dist paths and export subpaths must include the correct prefix (FR-030). |
| Cross-package | Import/export subpaths derived from namespace instead of file layout | Planned/Correction | Spec-mandated behavior; current implementation requires correction. |
| Serialization | `JsonSerializerContext` transpilation | Implemented | JSON names resolved at transpile time. |
| Validation | Generated type guards | Implemented | Via `[GenerateGuard]`. |
| Diagnostics | Stable `MS0001`-`MS0008` catalog | Implemented | See diagnostic catalog. |
| Cycles | Generated TS cyclic import detection | Implemented | Reported as `MS0005`. |

## Explicit Non-Core Guarantees

| Area | Feature | Status | Notes |
| --- | --- | --- | --- |
| Debuggability | Source-map-style C# -> TS debug tracing | Planned/Out of scope | Not currently a core product guarantee. |
| Coverage | Full unrestricted C# language support | Out of scope | Product intentionally supports a deliberate subset. |
| Runtime | Full .NET runtime simulation in browser | Out of scope | Contradicts core product thesis. |
