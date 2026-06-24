# Feature Support Matrix (Baseline)

What Metano supports **today**, with code + test traceability. Per
[../contracts/baseline-matrix.contract.md](../contracts/baseline-matrix.contract.md). Migrated from
`old-spec/08-feature-support-matrix.md` and extended with `Code area` + `Test` columns (every
`Implemented` row resolves to real code + a real test — SC-001).

Status legend: **Implemented** = part of the current product · **Partial** = supported with explicit
constraints · **Planned** = not a current guarantee. Unless a row names a backend, it describes the
**TypeScript** backend (the normative reference surface). Paths are repo-relative.

## Target Support

| Stage | Component | Status | Where |
| --- | --- | --- | --- |
| Frontend | C# / Roslyn (`CSharpSourceFrontend`) | Implemented (only frontend) | `src/Metano.Compiler/CSharpSourceFrontend.cs` |
| IR | Shared canonical representation | Implemented | `src/Metano.Compiler/IR/` |
| Backend port | `ITranspilerTarget` | Implemented | `src/Metano.Compiler/ITranspilerTarget.cs` |
| Backend | TypeScript | **Implemented** (reference) | `src/Metano.Compiler.TypeScript/` |
| Backend | Dart / Flutter | **Partial** | `src/Metano.Compiler.Dart/` |
| Backend | Kotlin / Swift / … | Planned | — (new project implementing `ITranspilerTarget`) |

## Type selection

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Selection | `[Transpile]`, `[TranspileAssembly]`, `[Ignore]` | TS | Implemented | `CSharpSourceFrontend.cs`, `src/Metano/Annotations/TranspileAttribute.cs` | `tests/Metano.Tests/AttributeTranspileTests.cs` | `[Ignore]` is the .NET-only boundary — transpilable code may not reference ignored types (MS0013). |

## Types

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Types | Records | TS | Implemented | `Extraction/IrClassExtractor.cs`, `Extraction/IrConstructorExtractor.cs` | `tests/Metano.Tests/RecordTranspileTests.cs` | — |
| Types | Classes + inheritance | TS | Partial | `Extraction/IrClassExtractor.cs`, `IR/IrTypeDeclaration.cs` | `tests/Metano.Tests/ClassInheritanceTests.cs` | Within the supported transpilation surface; not all CLR class features. |
| Types | Interfaces + generics | TS | Partial | `Extraction/IrInterfaceExtractor.cs`, `IR/IrTypeParameter.cs` | `tests/Metano.Tests/InterfaceTranspileTests.cs` | Supported for the mapped generic subset. |
| Types | Nullable ref/value types | TS | Implemented | `Extraction/IrTypeRefMapper.cs`, `IR/IrTypeRef.cs` | `tests/Metano.Tests/NullableTranspileTests.cs` | Lowered to `T \| null` unions (not `T?`/undefined). |
| Types | Inline wrappers (`[Branded]`/`[InlineWrapper]`) | TS | Implemented | `Extraction/IrExpressionExtractor.cs`, `src/Metano/Annotations/InlineWrapperAttribute.cs` | `tests/Metano.Tests/InlineWrapperTranspileTests.cs` | Branded/zero-cost primitives. |

## Enums & async

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Enums | Numeric enums | TS | Implemented | `Extraction/IrEnumExtractor.cs`, `IR/IrEnumMember.cs` | `tests/Metano.Tests/EnumTranspileTests.cs` | — |
| Enums | String enums (`[StringEnum]`) | TS | Implemented | `Extraction/IrEnumExtractor.cs`, `src/Metano/Annotations/StringEnumAttribute.cs` | `tests/Metano.Tests/FlagsEnumTests.cs` | — |
| Async | `Task` / `ValueTask` → `Promise` | TS | Implemented | `Extraction/IrTypeRefMapper.cs`, `IR/IrTypeRef.cs` | `tests/Metano.Tests/AsyncTranspileTests.cs` | — |
| Exceptions | Exception types + throw | TS | Partial | `IR/IrStatement.cs`, `Extraction/IrStatementExtractor.cs` | `tests/Metano.Tests/ExceptionTranspileTests.cs` | Within current transformation rules. |

## Expressions & pattern matching

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Pattern matching | `switch`, `is`, patterns | TS | Partial | `IR/IrPattern.cs`, `IR/IrExpression.cs` | `tests/Metano.Tests/SwitchPatternTranspileTests.cs` | Depends on current pattern handler support. |
| Expressions | Lambdas + interpolated strings | TS | Implemented | `IR/IrExpression.cs` (IrLambdaExpression), `Extraction/IrExpressionExtractor.cs` | `tests/Metano.Tests/LambdaTranspileTests.cs` | — |
| Expressions | Collection expressions `[]` | TS | Implemented | `Extraction/IrExpressionExtractor.cs`, `IR/IrExpression.cs` | `tests/Metano.Tests/CollectionInitTests.cs` | — |

## Modules

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Modules | `[NoContainer]` (`[ExportedAsModule]` legacy) | TS | Implemented | `CSharpSourceFrontend.cs`, `src/Metano/Annotations/NoContainerAttribute.cs` | `tests/Metano.Tests/NoContainerAttributeTranspileTests.cs` | `[ExportedAsModule]` deprecated. |
| Modules | `[ModuleEntryPoint]` | TS | Implemented | `CSharpSourceFrontend.cs`, `src/Metano/Annotations/ModuleEntryPointAttribute.cs` | `tests/Metano.Tests/ModuleEntryPointTests.cs` | Misuse → MS0006. |
| Modules | C# top-level statements | TS | Implemented | `CSharpSourceFrontend.cs`, `Extraction/IrModuleFunctionExtractor.cs` | `tests/Metano.Tests/ModuleEntryPointTests.cs` | — |

## Operators & overloads

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Operators | User-defined binary operators | TS | Partial | `IR/IrExpression.cs` (IrBinaryOp), `Extraction/IrExpressionExtractor.cs` | `tests/Metano.Tests/OperatorTranspileTests.cs` | When lowered by current operator rules. |
| Operators | User-defined unary operators | TS | Partial | `IR/IrExpression.cs` (IrUnaryOp), `Extraction/IrExpressionExtractor.cs` | `tests/Metano.Tests/OperatorTranspileTests.cs` | When explicitly mapped/named. |
| Operators | Prefix/postfix inc/dec | TS | Implemented | `IR/IrExpression.cs` (IrUnaryExpression), `Extraction/IrExpressionExtractor.cs` | `tests/Metano.Tests/IncrementExpressionTests.cs` | Preserves JS evaluation order. |
| Operators | Type-test + comparison lowerings | TS | Partial | `Extraction/IrExpressionExtractor.cs`, `IR/IrExpression.cs` (IrTypeCheck) | `tests/Metano.Tests/TemporalRelationalLoweringTests.cs` | Within current transformation surface. |
| Overloads | Method overload dispatch | TS | Implemented | `Extraction/IrExpressionExtractor.cs`, `CSharpSourceFrontend.cs` | `tests/Metano.Tests/MethodOverloadTests.cs` | Dispatcher + fast-path methods (ADR-0008). |
| Overloads | Constructor overload dispatch | TS | Implemented | `Extraction/IrConstructorExtractor.cs`, `Extraction/IrExpressionExtractor.cs` | `tests/Metano.Tests/ConstructorOverloadTests.cs` | Same strategy as methods. |

## Collections, LINQ, delegates

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Collections | `List<T>` / arrays | TS | Implemented | `Extraction/IrTypeRefMapper.cs`, `IR/IrTypeRef.cs` | `tests/Metano.Tests/ExpressionTranspileTests.cs` | — |
| Collections | `Dictionary` / `Map` | TS | Implemented | `Extraction/IrTypeRefMapper.cs`, `IR/IrTypeRef.cs` | `tests/Metano.Tests/DictionaryIndexerTests.cs` | — |
| Collections | `HashSet<T>` | TS | Implemented | `Extraction/IrTypeRefMapper.cs`, `IR/IrTypeRef.cs` | `tests/Metano.Tests/ExpressionTranspileTests.cs` | Requires runtime support. |
| Collections | `Queue` / `Stack` | TS | Implemented | `Extraction/IrTypeRefMapper.cs`, `IR/IrTypeRef.cs` | `tests/Metano.Tests/QueueStackTests.cs` | — |
| Collections | `ImmutableList` / `ImmutableArray` | TS | Implemented | `Extraction/IrTypeRefMapper.cs`, `Extraction/IrLinqMapping.cs` | `tests/Metano.Tests/DeclarativeMappingTests.cs` | Via immutable collection mappings. |
| LINQ | Core LINQ runtime layer | TS | Implemented | `IR/IrLinqChain.cs`, `Extraction/IrLinqChainFuser.cs`, `Extraction/IrLinqMapping.cs` | `tests/Metano.Tests/LinqFusionTests.cs` | Product-defined subset (ADR-0012). |
| Delegates | `Action`/delegate types | TS | Implemented | `Extraction/IrDelegateExtractor.cs`, `IR/IrExpression.cs` | `tests/Metano.Tests/DelegateTypeAliasTests.cs` | Emits function types. |
| Events | Event add/remove semantics | TS | Implemented | `Extraction/IrPropertyExtractor.cs`, `Extraction/IrExpressionExtractor.cs` | `tests/Metano.Tests/DelegateEventTests.cs` | Uses `delegateAdd`/`delegateRemove`. |
| BCL | `[MapMethod]`/`[MapProperty]` declarative mappings | TS | Implemented | `Mappings/DeclarativeMappingRegistry.cs`, `src/Metano/Annotations/MapMethodAttribute.cs` | `tests/Metano.Tests/TypeMappingTranspileTests.cs` | Keyed by (type, member); open generics (ADR-0003). |

## Output, packaging, serialization, diagnostics

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Output | Full-namespace file layout + per-namespace leaf barrels | TS | Implemented | `CSharpSourceFrontend.cs`, `Transformation/PathNaming.cs`, `Transformation/BarrelFileGenerator.cs` | `tests/Metano.Tests/OutputLayoutTests.cs`, `tests/Metano.Tests/ImportContractTests.cs`, `tests/Metano.Tests/DeterministicLayoutScenarioTests.cs` | Path = full kebab-cased namespace, no stripping; stable across the type set. Internal refs import files directly; root aggregation barrel opt-in (`--namespace-barrels`). ADR-0025 (supersedes ADR-0006 layout). |
| Output | Self-cleaning incremental builds (orphan pruning) | TS / core | Implemented | `TranspilerHost.cs` (`PruneOrphanedOutputs`), `Caching/CacheKeyBuilder.cs` | `tests/Metano.Tests/Caching/OrphanPruningTests.cs` | Reconciles the cache manifest; removes orphaned generated files + emptied dirs without `--clean`; transpiler-owned paths only. ADR-0025. |
| Packaging | `[EmitPackage]` cross-package + `package.json` | TS | Implemented | `CSharpSourceFrontend.cs`, `Metano.Compiler.TypeScript/PackageJsonWriter.cs` | `tests/Metano.Tests/EmitPackageTests.cs` | Dependency propagation (ADR-0011). |
| Serialization | `JsonSerializerContext` | TS | Implemented | `Extraction/IrExpressionExtractor.cs`, `IR/IrRuntimeRequirement.cs` | `tests/Metano.Tests/JsonSerializerContextTests.cs` | JSON names resolved at transpile time. |
| Validation | Type guards (`[GenerateGuard]`) | TS | Implemented | `CSharpSourceFrontend.cs`, `src/Metano/Annotations/GenerateGuardAttribute.cs` | `tests/Metano.Tests/TypeGuardTranspileTests.cs` | Emits `isT` + `assertT` (ADR-0009). |
| Diagnostics | Stable `MS0001`–`MS0028` | TS | Implemented | `Diagnostics/MetaSharpDiagnostic.cs` | `tests/Metano.Tests/DiagnosticsTests.cs` | Full catalog: [diagnostic-catalog.md](./diagnostic-catalog.md). MS0026 = unrecognized JSX renderable; MS0027/MS0028 = JS-interop misuse. |
| Cycles | Generated cyclic import detection | TS | Implemented | `CSharpSourceFrontend.cs`, `Analysis/` | `tests/Metano.Tests/CyclicReferenceTests.cs` | Reported as MS0005. |
| Assets | `<MetanoAsset>` static asset copy | TS | Implemented | `AssetFlagParser.cs`, `AssetSpec.cs` | `tests/Metano.Tests/Caching/TranspilerHostAssetCopyTests.cs` | Cache-aware; failures → MS0025 (FR-048). |

## JS-interop primitives

Declarative array-tuple / callable shapes + tuple deconstruction (no hand-written `[Emit]`). Spec: [specs/003-js-interop-primitives/](../../003-js-interop-primitives/spec.md).

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Interop | `[JsTuple]` record → JS array-tuple | TS | Implemented | `Bridge/IrToTsJsTupleBridge.cs`, `Annotations/TypeScript/JsTupleAttribute.cs` | `tests/Metano.Tests/JsTupleTranspileTests.cs` | Type alias / erased with `[Import]`; positional `[i]`; misuse → MS0027. |
| Interop | `[JsCallable]` interface → direct invocation | TS | Implemented | `Bridge/IrToTsExpressionBridge.cs`, `Annotations/TypeScript/JsCallableAttribute.cs` | `tests/Metano.Tests/JsCallableTranspileTests.cs` | `recv.Invoke(a)`→`recv(a)`; overloaded `Invoke`; erased; misuse → MS0028. |
| Interop | Tuple deconstruction `var (a,b)=e` | TS | Implemented | `Extraction/IrStatementExtractor.cs`, `TypeScript/AST/TsDestructuringDeclaration.cs` | `tests/Metano.Tests/DeconstructionTranspileTests.cs` | → `const [a, b] = e`; discards supported; flat only. |

## UI components (JSX/TSX)

First vertical slice of UI-component transpilation — C# renderable record components → idiomatic SolidJS TSX. Spec: [specs/002-jsx-codegen-from-csharp/](../../002-jsx-codegen-from-csharp/spec.md). Proving target SolidJS; recognition is library-agnostic (validated against an imported `solid-router` type).

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| UI | Renderable record component → `export function` + `<Name>Props` | TS | Implemented | `Bridge/IrToTsJsxComponentBridge.cs`, `Annotations/TypeScript/JsxComponentBuilderAttribute.cs` | `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` | Settable/positional props → optional props; body hoists prop defaults. |
| UI | Native elements (`[JsxNativeElement]`) → intrinsic JSX | TS | Implemented | `Bridge/IrToTsJsxBridge.cs`, `TypeScript/AST/TsJsxElement.cs` | `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` | Attr name = `[Name]` override else camelCase; `Children` slot resolved by symbol. |
| UI | Component composition (`<Name … />`) + render-entry lambda | TS | Implemented | `Bridge/IrToTsJsxBridge.cs` | `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` | Type-driven (constructed type `RendersAsJsxElement`). |
| UI | SolidJS reactivity (`ISignal`/`CreateSignal`/`CreateEffect`/`For`/`render`) | TS | Implemented | `bindings/Metano.TypeScript.SolidJs/`, `Bridge/IrToTsJsxBridge.cs` | `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` | Explicit signal API only; wrapper elided to Solid tuple. No auto field-mutation reactivity. |
| UI | Imported renderables (`[Import]`, e.g. `solid-router`) | TS | Implemented | `Bridge/IrToTsJsxBridge.cs`, `Extraction/IrExpressionExtractor.cs` | `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` | Library-agnostic; emits `<Name/>` + package import. |
| UI | `.tsx` emission + `MS0026` unrecognized renderable | TS | Implemented | `Transformation/{PathNaming,TypeTransformer}.cs`, `CSharpSourceFrontend.cs` | `tests/Metano.Tests/SolidJsJsxTranspileTests.cs` | `.tsx` only when JSX present; sample `targets/js/sample-solid-ui`. |

## Dart backend — status & gaps

| Area | Feature | Backend | Status | Code area | Test | Constraints |
| --- | --- | --- | --- | --- | --- | --- |
| Dart | Types, members, constructors, covered expr/stmt subset | Dart | Partial | `src/Metano.Compiler.Dart/Bridge/`, `Transformation/DartTransformer.cs` | `tests/Metano.Tests/DartBackendTests.cs` | Lowers through the shared IR. |
| Dart | Classic extension methods | Dart | Planned (gap) | `src/Metano.Compiler.Dart/Bridge/` | `tests/Metano.Tests/DartBackendTests.cs` | Not yet supported — RT-02. |
| Dart | `[ModuleEntryPoint]` body lowering | Dart | Planned (gap) | `src/Metano.Compiler.Dart/` | `tests/Metano.Tests/DartBackendTests.cs` | Body not yet lowered — RT-02. |
| Dart | JSON serializer context | Dart | Planned (gap) | `src/Metano.Compiler.Dart/Bridge/` | `tests/Metano.Tests/DartBackendTests.cs` | Not yet emitted — RT-02. |

## Explicit non-guarantees

| Area | Feature | Status |
| --- | --- | --- |
| Debuggability | Source-map-style C# → target debug tracing | Planned / out of scope |
| Coverage | Full unrestricted C# language support | Out of scope (deliberate subset) |
| Runtime | Full .NET runtime simulation | Out of scope |

> Path prefix note: under `src/Metano.Compiler/`, the `Extraction/`, `IR/`, `Analysis/`, `Mappings/`,
> `Diagnostics/` folders are relative to that project root.
