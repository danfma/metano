# Feature Specification: C# 15 union declarations

**Feature Branch**: `spec/009-csharp-unions` (spec directory `009-csharp-unions`)

**Created**: 2026-10-31

**Status**: Draft

**Input**: GitHub issue [danfma/metano#304](https://github.com/danfma/metano/issues/304), "Support C# 15 union declarations". Research notes from the video-studio Phase 4 investigation cover these sections: `stj-union`, `roslyn-union-api`, `host-net11`, `pattern-lowering-map`, the `csharp-unions` design and its critic. They are summarized with evidence tags in [research.md](./research.md).

**Traceability**: New capability, the spec change request for #304. It extends three legacy requirement families:
- semantic transformation, `FR-005`–`FR-014`, specifically pattern matching (`FR-013`, cited the same way by #279 / PR #298);
- serialization, `FR-032`–`FR-034`;
- diagnostics, `FR-038`–`FR-040`.

It also adds rows to the baseline [feature-support matrix](../001-project-baseline-evolution/baseline/feature-support-matrix.md) and reserves `MS0031`–`MS0036` in the [diagnostic catalog](../001-project-baseline-evolution/baseline/diagnostic-catalog.md). As in features 002–008, the FR identifiers below are local to this feature and are cited as "009 FR-0NN".

## Overview

C# 15 (preview, .NET 11 / SDK 11 RC) adds nominal type unions:

```csharp
public record ImageClip(string Path, double Seconds);
public record SilenceClip(double Seconds);

public union Clip(ImageClip, SilenceClip);
public union Id(int, string);
```

The compiler lowers each union to a sealed struct with `[Union]` that implements `IUnion`. The struct has one `object? Value` property and one implicit constructor per case. A case value converts to the union implicitly. A `switch` that covers every case is exhaustive with no warning. System.Text.Json (STJ) 11 serializes a union as its **bare case payload**.

Metano cannot handle this today:
- It pins Roslyn 5.3.0. That parser reads `union` as an identifier, so the run fails with MS0009 and produces no output.
- A union that comes from a referenced assembly would go down the struct path and be emitted as a struct wrapper with a `value` field. That is the wrong shape for both TypeScript and JSON.

This feature makes unions a first-class, target-agnostic IR concept. The TypeScript target **erases** them to a plain type union, and the JSON wire format mirrors .NET 11 STJ exactly.

## Maintainer decisions (resolved)

- **D1 — Erase the wrapper.** `union Clip(ImageClip, SilenceClip)` emits `export type Clip = ImageClip | SilenceClip;`. No class, no wrapper object, no runtime value for the union itself.
- **D2 — Case dispatch by runtime test.** Each case test uses the case's own kind:
  - `instanceof` for transpilable classes and records;
  - `typeof` (or the runtime `is*` primitive helpers) for primitives;
  - the generated `isX` guard for `[PlainObject]` and interface cases.

  The decision comes from the single runtime-type-test classification (`IrRuntimeTypeTest`, PR #298), never from a second, union-specific rule.
- **D3 — Exhaustive switch ends with a `never` check.** A switch over a union that tests every case ends with `$s satisfies never;`, followed by the existing non-exhaustive throw. A missing case therefore fails the TypeScript build, and `default(U)` still fails at runtime, as it does in C#.
- **D4 — Identity conversions.** The implicit case→union conversion and an explicit `(U)x` cast emit nothing. `u.Value` and `new U(x)` emit their operand unchanged.
- **D5 — JSON mirrors .NET 11 STJ strictly.**
  - A value serializes as its bare case payload: no wrapper, no discriminator.
  - Deserialization picks the case by JSON token kind only.
  - When two or more cases share a token kind (for example two object-shaped records), the build reports a diagnostic, the analogue of STJ's SYSLIB1227, and reading such a value fails at runtime with STJ's message.
- **D6 — Host stays net10.** The CLI tools keep `net10.0` and gain `<RollForward>Major</RollForward>`, so they also run on machines that have only .NET 11 installed. There is no multi-targeting and no `dotnet run --framework` change.
- **D7 — Repo SDK moves to 11 RC.** The repo `global.json` pins the SDK 11 RC with `allowPrerelease`. Union tests and the union sample compile against the net11 reference assemblies (`IUnion` / `UnionAttribute` live in `System.Runtime` 11). CI installs both 10.0.x and 11.0.x.
- **D8 — Roslyn 5.9.0.** `Microsoft.CodeAnalysis.*` moves from 5.3.0 to 5.9.0.
  - `ITypeSymbol.IsUnion` and `Conversion.IsUnion` are experimental (`RSEXPERIMENTAL006`).
  - The public API exposes no case list, so cases are read from the union's public one-parameter instance constructors. A source union rejects user-declared ones with CS9374, which makes this reliable.
- **D9 — Diagnostic codes.** MS0029 and MS0030 are taken (spec 008). This feature takes the next free block, **`MS0031`–`MS0036`**.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A union becomes an erased TypeScript union type (Priority: P1)

A library author declares `public union Clip(ImageClip, SilenceClip);` in a transpiled project. The generated TypeScript declares `Clip` as a type-only union of the case types. Code that receives a `Clip` receives the case value directly.

**Why this priority**: Without the declaration, nothing else (dispatch, JSON, cross-package) can be expressed. On its own, this story already unblocks consumers that only pass unions through.

**Independent Test**: Transpile the snippet above. `clip.ts` contains exactly `export type Clip = ImageClip | SilenceClip;` with `import type` lines for both cases, and `tsgo --strict` accepts a function `(c: Clip) => c`.

**Acceptance Scenarios**:

1. **Given** `union Clip(ImageClip, SilenceClip)`, **When** transpiled, **Then** the output is `export type Clip = ImageClip | SilenceClip;`, with no class, no `value` field, and no runtime export.
2. **Given** `union Id(int, string)`, **When** transpiled, **Then** the output is `export type Id = number | string;`.
3. **Given** `union NId(int?, string)`, **When** transpiled, **Then** the output is `export type NId = number | null | string;`.
4. **Given** `union Opt<T>(T, None) where T : notnull`, **When** transpiled, **Then** the output is `export type Opt<T> = T | None;`.
5. **Given** a `Clip?` property (`Nullable<Clip>`), **When** transpiled, **Then** its type is `Clip | null`.
6. **Given** `Clip c = new SilenceClip(2);`, `new Clip(x)`, `(Clip)x` and `c.Value`, **When** transpiled, **Then** each one emits only its operand (`new SilenceClip(2)`, `x`, `x`, `c`).
7. **Given** `[assembly: TranspileAssembly]` and a public union, **When** transpiled, **Then** the union is picked up like any other public type.

### User Story 2 - Pattern matching over a union is type-checked and exhaustive (Priority: P1)

A developer writes `c switch { ImageClip i => …, SilenceClip s => … }`. The TypeScript output tests each case with its runtime test and binds the designators. Because the switch ends with a `never` check, TypeScript rejects the build if a case is later added to the union but not to the switch.

**Why this priority**: Matching is how unions are consumed. A mismatch between C# exhaustiveness and TS behavior would silently break dispatch.

**Independent Test**: Transpile `Describe(Clip)` and `Show(Id)` (see [contracts/ts-emission.md](./contracts/ts-emission.md)) and run `tsgo --strict`; it passes. Remove one arm from the TS output by hand; `tsgo` reports TS1360 at the `satisfies never` line.

**Acceptance Scenarios**:

1. **Given** a switch expression over `Clip` with one unguarded type arm per case, **When** transpiled, **Then** the IIFE has one `if ($s instanceof …)` per case, then `$s satisfies never;`, then `throw new Error("Non-exhaustive switch expression");`.
2. **Given** a switch over `Id(int, string)`, **When** transpiled, **Then** the tests are the primitive tests that `IrRuntimeTypeTest` selects (`typeof $s === "number"`, `typeof $s === "string"`), followed by the same tail.
3. **Given** an arm with a `when` clause, a property pattern or a positional pattern, **When** transpiled, **Then** the `never` check is omitted (TS cannot prove coverage) and only the throw remains.
4. **Given** a switch over `Clip?`, or over a union with a nullable case, that has **no** `null` arm, **When** transpiled, **Then** the `never` check is omitted and only the throw remains, so the TS build does not fail where C# only warns (CS8655).
5. **Given** the same switch **with** a `null =>` arm, **When** transpiled, **Then** the null arm is tested first (`$s === null`) and the `never` check is emitted.
6. **Given** `c is ImageClip i && i.Seconds > 1`, **When** transpiled, **Then** the output is `c instanceof ImageClip && c.seconds > 1` (binding as today's `is` lowering does).
7. **Given** a `switch` **statement** with `case ImageClip i:` / `case SilenceClip s:` labels, **When** transpiled, **Then** each label is lowered to its type test with its binding, never to `default:`. A statement gets no `never` check and no throw, because C# statement switches are not exhaustive.
8. **Given** `[GenerateGuard]` on a union, **When** transpiled, **Then** `isClip(v): v is Clip` is emitted as the OR of the case tests.

### User Story 3 - JSON round-trips byte-for-byte with .NET 11 STJ (Priority: P2)

A service written in .NET 11 serializes a `Holder(Clip Clip, Id Id)` with STJ, and the TypeScript client generated by Metano deserializes it. The client serializes it back, and the bytes equal what .NET wrote.

**Why this priority**: Wire parity is the reason most consumers share types between C# and TS. It comes after US1/US2 because it builds on the case tests.

**Independent Test**: Use fixtures captured from a .NET 11 STJ probe (`{"Clip":{"Seconds":3},"Id":"abc"}`, `5`, `"x"`, `null`, a list, a map). The generated context deserializes each fixture, and serializing the result reproduces the fixture byte-for-byte. Ambiguous and unsupported inputs throw errors whose messages match STJ's.

**Acceptance Scenarios**:

1. **Given** `Id` and the value `5`, **When** serialized, **Then** the output is `5`; for `"x"` it is `"x"`; for `null` it is `null`.
2. **Given** `Holder(new SilenceClip(3), "abc")`, **When** serialized, **Then** the output is `{"Clip":{"Seconds":3},"Id":"abc"}`, with no wrapper and no discriminator.
3. **Given** `Id` and the input `5.5`, `true` or `{}`, **When** deserialized, **Then** the error message matches STJ's (see [contracts/json-wire.md](./contracts/json-wire.md)).
4. **Given** `Clip(ImageClip, SilenceClip)` reachable from a `JsonSerializerContext`, **When** transpiled, **Then** `MS0035` (warning) names the union and the shared token kind `Object`. At runtime, deserializing any object for `Clip` throws `JSON value type 'Object' is ambiguous for union type 'Clip' …`, exactly as STJ does. Serialization still works.
5. **Given** a union inside `List<T>`, `Dictionary<string, T>`, `T?`, or a `[PlainObject]` DTO field, **When** round-tripped, **Then** the union descriptor composes with the container descriptor and matches STJ.

### User Story 4 - Unions cross package boundaries (Priority: P2)

A union declared in an `[EmitPackage]` library is used by a consumer project. The consumer imports the union as a type and imports the case classes as values wherever it tests them.

**Independent Test**: `TranspileWithLibrary(lib with Clip + cases, consumer with Describe(Clip))`. The consumer has `import type { Clip } from "lib/…"`, value imports for `ImageClip` and `SilenceClip`, and the library dependency in `package.json`.

**Acceptance Scenarios**:

1. **Given** a library union read from metadata (constructors marked `[CompilerGenerated]`, `IsImplicitlyDeclared = false`), **When** the consumer is transpiled, **Then** the union semantics and cases are identical to the in-source case.
2. **Given** a library `[PlainObject]` case with `[GenerateGuard]`, **When** the consumer tests it, **Then** `isX` is imported from the library package (this closes today's local-only guard-import gap).
3. **Given** a consumer that uses a library union without `LangVersion preview`, **When** transpiled, **Then** Roslyn's CS8652 surfaces through MS0009 with the C# message unchanged.

### User Story 5 - Shapes that erasure cannot represent faithfully are rejected (Priority: P2)

A developer writes a union whose C# meaning cannot survive erasure, for example `union N(int, double)`, where both cases are JS `number`. The build fails with a specific diagnostic instead of producing TypeScript that dispatches differently from C#.

**Acceptance Scenarios**:

1. **Given** `union N(int, double)`, `union S(IShape, Square)` (Square implements IShape), or `union E(object, string)`, **When** transpiled, **Then** `MS0032` (error) names the two cases.
2. **Given** a union with body members, an extra interface, a union-typed case, or `[PlainObject]` / `[Branded]` / `[InlineWrapper]` / `[StringEnum]` / `[ExportedAsModule]` / `[JsTuple]` on the union, **When** transpiled, **Then** `MS0031` (error) states the reason.
3. **Given** an interface or `[PlainObject]` case without `[GenerateGuard]`, or a case type with no runtime test, **When** a test against it is needed, **Then** `MS0033` (error) is reported.
4. **Given** `default(Clip)` or `default` in a `Clip` position, **When** transpiled, **Then** `MS0034` is reported (severity is an open decision, OD-3).
5. **Given** `clip.ToString()`, `$"{clip}"`, `clip.GetType()`, or `o is Clip` where `o` is `object`, **When** transpiled, **Then** `MS0036` (warning) states that the erased output differs from C#.

### User Story 6 - Dart does not crash on unions (Priority: P3)

**Acceptance Scenarios**:

1. **Given** a union in a Dart run, **When** transpiled, **Then** the output contains `/// union: ImageClip | SilenceClip` and `typedef Clip = Object;` (`Object?` if any case is nullable). There is no MS0009 and no exception.
2. **Given** a switch expression over a union, **When** transpiled for Dart, **Then** it ends with `_ => throw StateError('Non-exhaustive switch expression')`. Pattern labels in statements are lowered and never printed as `default:`.

### Edge Cases

- **`default(U)` / zero-initialized unions.** In C#, `Value == null`. An exhaustive switch over it throws `SwitchExpressionException`, and the TS throw tail reproduces this. `default` in a non-null TS position does not type-check under strict mode, so see OD-3.
- **`Nullable<U>.Value` vs `U.Value`.** On a `Clip?`, `x.Value` is `Nullable<Clip>.Value`, which returns a `Clip`. The identity rewrite in D4 MUST apply only to the union's own `Value` property symbol. On a `Clip?`, `x?.Value` reads the union's `Value` through a conditional access and rewrites to `x`.
- **Boxing.** `object o = clip; o is ImageClip` is **false** in C#, because the box holds a `Clip`, but **true** after erasure. Conversely, `object o = new ImageClip(…); o is Clip` is false in C# but true in TS. Both are reported as MS0036 divergences (OD-6 decides whether v1 detects the boxing conversion itself).
- **Value semantics.** `u1.Equals(u2)` and `u.GetHashCode()` compare `Value` structurally in C# (`ValueType`). They lower to the runtime `equals(a, b)` and hash helpers, which handle primitives, never to `u.equals(…)`, which would crash on a `number`. `HashSet<Id>` and dictionary keys rely on this.
- **Nested unions.** For `union Outer(Inner, A)` with `union Inner(int, string)`, C# keeps `Inner` boxed (`o is int` is CS0184, never true), and STJ writes `5` for `Outer(Inner 5)` but cannot read `5` back. Flattening would change C# semantics, so v1 rejects union-typed cases with MS0031.
- **Order of cases** follows declaration order (constructor member order). The TS alias, the dispatch and the JSON write all use it.
- **Generic unions.** The alias is always emitted. Pattern tests or JSON against an open type-parameter case raise MS0033 (the negation-of-siblings trick is deferred).
- **Hand-written `[Union] struct/class : IUnion`.** Roslyn reports `IsUnion = true`, and detection runs before the `TypeKind` dispatch, so a `[Union] class` is covered too. Its user members fail the shape check (MS0031). A plain `struct : IUnion` without `[Union]` reports `IsUnion = false` and stays an ordinary struct.
- **A union case that is a `[JsonPolymorphic]` base.** STJ handles it with a `$type` discriminator. The TS side depends on #268 (polymorphic JSON), so until then it gets MS0031 in a JSON-reachable position (OD-7).
- **`[JsonUnion(TypeClassifier = …)]`** and `JsonUnionTypeStructuralClassifier`. A classifier is C# code over `Utf8JsonReader` and cannot be transpiled, so it gets MS0031 when JSON-reachable.
- **Union as a `[JsonSerializable(typeof(U))]` root.** This depends on the non-object root fix (`json-collection-root`, `ValueSpec`). It is out of scope until that lands (OD-8).
- **Experimental API drift.** RSEXPERIMENTAL006 APIs and STJ RC messages may change before GA. Only one core file reads the experimental API, and the STJ messages live in one runtime module.

## Requirements *(mandatory)*

### Functional Requirements

Toolchain:

- **FR-001**: `Microsoft.CodeAnalysis.CSharp`, `.CSharp.Workspaces` and `.Workspaces.MSBuild` MUST be upgraded to 5.9.0. Outside the union work, the existing suite and every `targets/` output MUST be unchanged by the upgrade.
- **FR-002**: The CLI tool projects (`Metano.Compiler.TypeScript`, `Metano.Compiler.Dart`) MUST keep `net10.0` as their only target framework and MUST set `<RollForward>Major</RollForward>`, so the installed tool starts on a machine where only a newer runtime (.NET 11) is installed.
- **FR-003**: The repo `global.json` MUST pin the SDK 11 RC with `allowPrerelease: true`. Every existing net10.0 sample, test and package MUST build and produce byte-identical `targets/` output under it.
- **FR-004**: CI (`ci.yml`) and release (`release.yml`) MUST install both the 10.0.x and the 11.0.x SDKs. The 10 runtime stays because the tests and the CSharpier local tool run on it.

Detection and IR (core, target-agnostic):

- **FR-005**: The core MUST detect a union only through `ITypeSymbol.IsUnion`, in one adapter file that is the sole place allowed to use `RSEXPERIMENTAL006` APIs. Detection MUST run before any `TypeKind` dispatch, in both the TypeScript and Dart transformers.
- **FR-006**: Union cases MUST be read as the parameter types of the union's public, non-static, single-parameter instance constructors, in member (declaration) order. This MUST work identically for source and metadata symbols and for generic unions.
- **FR-007**: The IR MUST represent a union declaration as its own declaration kind (`IrUnionDeclaration`: name, visibility, ordered case type refs, type parameters, attributes). A union reference MUST carry a `Union` semantic kind with its cases. This is a language-level construct, not a BCL-specific node.
- **FR-008**: The IR switch expression MUST carry the scrutinee type, with its nullability. Switch-statement sections MUST carry a pattern and an optional `when` clause, so `case T x:` labels are represented rather than dropped.
- **FR-009**: The core MUST rewrite these identities once, for every target:
  - the union `Value` property read (`u.Value`, `u?.Value`) becomes the receiver;
  - `new U(x)` becomes `x`;
  - the implicit and explicit case→union conversions emit nothing.
- **FR-010**: Union-typed values MUST lower `Equals` / `GetHashCode` to the runtime equality and hash helpers that accept primitives.
- **FR-011**: The runtime-requirement scanner MUST walk type patterns (expressions and statements) and union declarations, so every runtime helper a case test needs (e.g. `isInt32`, a guard) is imported.

Validation (core, at declaration, once per type):

- **FR-012**: `MS0031 UnsupportedUnionShape` (Error) MUST be reported in any of these cases:
  - the union declares body members or interfaces beyond `IUnion`;
  - a case is itself a union;
  - the union carries `[PlainObject]`, `[Branded]`, `[InlineWrapper]`, `[StringEnum]`, `[ExportedAsModule]` or `[JsTuple]`;
  - a JSON-reachable union uses `[JsonUnion(TypeClassifier = …)]`, or has a `[JsonPolymorphic]` case before #268 lands.

  `[Name]`, `[EmitInFile]`, `[NoEmit]`, `[Ignore]`, `[Transpile]` and `[GenerateGuard]` MUST be supported.
- **FR-013**: `MS0032 IndistinguishableUnionCases` (Error) MUST be reported when one case is implicitly convertible to another (Roslyn conversion classification), or when two cases share one JS runtime test as classified by `IrRuntimeTypeTest`. Examples: `int`/`double`, `int`/`long` where both are `number`, `char`/`string`, `Guid`/`string`, a `[Branded]` string and `string`.
- **FR-014**: `MS0033 UntestableUnionCase` (Error) MUST be reported when a case test is required and the case classifies as `None`:
  - an interface or `[PlainObject]` without `[GenerateGuard]`;
  - a type parameter;
  - an array or collection;
  - a non-transpilable class without `[Import]` / `[External]`.

  For non-generic unions the check runs at the declaration. For a type-parameter case it runs at the pattern or JSON use site (OD-4).
- **FR-015**: `MS0034 UnionDefaultValue` MUST be reported for `default(U)`, a target-typed `default` in a union position, and a non-nullable union field or auto-property that no initializer and no constructor assigns. Emission and severity follow OD-3.
- **FR-016**: `MS0036 UnionErasureDivergence` (Warning) MUST be reported in these cases, and the output MUST still be emitted:
  - `ToString()` on a union;
  - string interpolation of a union;
  - `GetType()` on a union;
  - `is U` / `as U` / a `U` type pattern over an input whose static type is not `U` or `U?`.

TypeScript target:

- **FR-017**: A union declaration MUST emit `export type <Name><TypeParams> = <Case₁> | … | <Caseₙ>;`. A nullable case contributes `| null` in its position. Case types MUST be imported with `import type` in the declaring file.
- **FR-018**: Union type references MUST map to the alias name. `Nullable<U>` maps to `U | null`, using the existing nullable mapping.
- **FR-019**: Every runtime test against a union case MUST come from the shared `IrRuntimeTypeTest` classification (#279 / PR #298): `instanceof`, `typeof` / primitive helper, or guard. A test against the union itself (`o is U`, `[GenerateGuard]` on U) MUST be the OR of its case tests.
- **FR-020**: A switch **expression** MUST emit `$s satisfies never;` before the existing throw when all of these hold:
  - its scrutinee type is a union;
  - every arm before the catch-all is an unguarded type pattern on a distinct case, or a `null` constant;
  - there is no catch-all;
  - either the scrutinee and every case are non-nullable, or a `null` arm exists.

  Otherwise only the throw is emitted. The throw message stays `"Non-exhaustive switch expression"`, so existing goldens are unchanged.
- **FR-021**: A switch statement with pattern labels (`case T x:`, with or without `when`) MUST lower to an `if` / `else if` chain over a once-evaluated subject, with designator bindings. It has no `never` check and no throw, because C# statement switches are not exhaustive. This also fixes today's silent drop of `case T x:` labels in non-union code.
- **FR-022**: Cross-package unions MUST resolve semantics and cases from metadata (FR-006). The alias is imported as a type, case classes used in `instanceof` as values, and guards from the producing package. This extends guard-import resolution, which today is local-only, to the cross-assembly origin map.

JSON (TypeScript target + runtime):

- **FR-023**: The generated `SerializerContext` MUST describe a union-typed value with a `union` `TypeDescriptor` that carries the union name and, per case in declaration order:
  - the case's JSON token kind;
  - its runtime test;
  - a thunk to the case's descriptor.

  The descriptor MUST compose inside `array`, `map`, `hashSet` and `nullable` descriptors and in `[PlainObject]` DTO fields.
- **FR-024**: Serialization MUST write `null` for `null`. Otherwise it MUST write the bare payload of the first case whose runtime test matches, with no wrapper and no discriminator, using that case's descriptor.
- **FR-025**: Deserialization MUST classify by JSON token kind only.
  - `null` returns `null`.
  - Zero candidate cases throws `JSON token type '<Token>' is not supported for union type '<U>'.`
  - More than one candidate throws `JSON value type '<Kind>' is ambiguous for union type '<U>' because multiple case types can use this value type. Specify a custom type classifier to support deserialization.`
  - Exactly one candidate delegates to that case.

  Token and kind names follow STJ (`Number`, `String`, `True`, `False`, `StartObject`/`Object`, `StartArray`/`Array`); the exact table is in [contracts/json-wire.md](./contracts/json-wire.md).
- **FR-026**: `MS0035 AmbiguousUnionJsonCases` (Warning) MUST be reported once per union that is reachable from a `JsonSerializerContext` and has two or more cases with the same JSON token kind. The message names the cases and the kind, mirroring SYSLIB1227. Generation continues.

Dart target:

- **FR-027**: Dart MUST emit `typedef <Name> = Object;` (`Object?` if any case is nullable), preceded by a `/// union: A | B` doc comment. Switch expressions over a union MUST end with `_ => throw StateError('Non-exhaustive switch expression')`. Statement pattern labels MUST be lowered and never printed as `default:`. Unions MUST NOT raise MS0009 or throw in the Dart target.

Tests and CI:

- **FR-028**: Tests MUST cover every acceptance scenario above:
  - golden files under `tests/Metano.Tests/Expected/` for TS emission;
  - one test per diagnostic reason (MS0031–MS0036);
  - source-vs-metadata union reading;
  - `TranspileWithLibrary` cross-package output;
  - Dart shape tests.

  Inline test compilations of union sources MUST reference the net11 reference assemblies.
- **FR-029**: A sample `samples/SampleUnions` (net11.0, `LangVersion preview`) and its consumer `targets/js/sample-unions` MUST prove:
  - with `bun run build` (tsgo strict), that the output compiles, including the `never` checks;
  - with `bun test`, dispatch, the `default` throw, and JSON byte parity against fixtures produced by real .NET 11 STJ.

  CI MUST run that consumer's build and tests explicitly.

### Non-Functional Requirements

- **NFR-001 (no churn)**: For sources without unions, generated output MUST be byte-identical to `main`. The only exception is FR-021's switch-statement pattern-label fix, whose previous output was wrong. It is classified as a `fix:` in the release notes.
- **NFR-002 (containment)**: `RSEXPERIMENTAL006` MUST be suppressed in exactly one core file, with a file-local pragma and no project-wide `NoWarn`. STJ message texts MUST live in one runtime module.
- **NFR-003 (target-agnostic core)**: Detection, case reading, validation and the identity rewrites live in `Metano.Compiler`. The TS and Dart targets only render.
- **NFR-004 (single classification)**: No second runtime-type-test decision table is introduced. Pattern lowering, union guards, JSON case tests and the scanner all consume `IrRuntimeTypeTest`.
- **NFR-005 (determinism)**: Case order, diagnostic order and the emitted output are stable across runs and independent of whether the union comes from source or from metadata.

### Key Entities

- **Union declaration**: a named, possibly generic, ordered set of case types. It has no members of its own in v1.
- **Union case**: one case type, with its runtime test classification and its JSON token kind.
- **Union semantics**: the `Union` kind plus cases, attached to every reference to the union, so backends decide without Roslyn.
- **Union descriptor (runtime)**: the JSON `TypeDescriptor` variant that implements STJ's token-kind classification.

## Success Criteria *(mandatory)*

- **SC-001**: The issue's `Clip` / `Id` example transpiles to TS that passes `tsgo --strict`, with 0 `/* TODO */`, 0 wrapper classes and 0 occurrences of `.value` on a union.
- **SC-002**: Removing one arm from a union switch in the generated TS makes `tsgo` fail (TS1360). `default(Clip)` passed to the generated `describe` throws `Non-exhaustive switch expression`.
- **SC-003**: For 100% of the JSON fixtures produced by .NET 11 STJ, TS deserialize followed by serialize reproduces the fixture bytes, and every STJ error fixture yields the same message.
- **SC-004**: All existing goldens and `targets/` outputs are unchanged after the toolchain bump and the feature, except the reviewed FR-021 fix diff.
- **SC-005**: Each MS0031–MS0036 reason has a test that asserts its code, severity and location.

## Open decisions

Each item carries a recommendation. None blocks the toolchain stage (FR-001–FR-004).

- **OD-1 — Test project framework.** The inline union compilations need net11 reference assemblies (D7). Two options:
  - (a) Keep `Metano.Tests` on `net10.0` and feed `TranspileHelper` the `Microsoft.NETCore.App.Ref` 11 pack for union tests. This is *recommended*: no RC runtime is needed for the whole suite.
  - (b) Move `Metano.Tests` to `net11.0`.
- **OD-2 — JSON fixture provenance.** The fixtures are captured from real .NET 11 STJ. Two options:
  - (a) A small net11 console project regenerates them in CI, followed by a `git diff --exit-code` check. This is *recommended*: it catches STJ RC changes.
  - (b) A documented manual script, with the fixtures checked in.
- **OD-3 — `default(U)`.** Three options:
  - (a) MS0034 **error** in v1. This is *recommended*: `null` does not type-check at non-null TS positions, and the project's value-object rule already treats `default` as invalid.
  - (b) Emit `null` plus a warning.
  - (c) Emit `null as unknown as U` plus a warning.
- **OD-4 — Where MS0033 fires.** For non-generic unions it fires at the declaration (*recommended*); for type-parameter cases it fires at the use site. The alternative is to fire always at the use site, which allows pass-through-only unions over untestable cases.
- **OD-5 — Body members and extra interfaces.** Two options:
  - (a) Reject them in v1 with MS0031. This is *recommended*.
  - (b) Lower them to a companion `namespace Clip { export function … (self: Clip) }` and rewrite the call sites.
- **OD-6 — Boxing detection for MS0036.** Two options:
  - (a) Warn only on the syntactic sites in FR-016 in v1. This is *recommended*.
  - (b) Also warn at every union→`object`/interface conversion (`Conversion.IsBoxing` on a union source).
- **OD-7 — `[JsonPolymorphic]` cases.** Two options:
  - (a) MS0031 until #268 ships, then support it. This is *recommended*.
  - (b) Make #268 a hard prerequisite for the JSON stage.
- **OD-8 — Union JSON roots.** Two options:
  - (a) Defer `[JsonSerializable(typeof(U))]` roots until the non-object root work (`ValueSpec`, batch-3 `json-collection-root`) lands. Union-typed properties and container elements are in scope now. This is *recommended*.
  - (b) Do the root work inside this feature.
- **OD-9 — Implicit guards.** Two options:
  - (a) Require an explicit `[GenerateGuard]` on `[PlainObject]` / interface cases (MS0033). This is *recommended*: it is explicit and keeps guard emission opt-in.
  - (b) Synthesize a guard for every such case.
- **OD-10 — Dart typedef.** Two options:
  - (a) `typedef Clip = Object;`. This is *recommended*: it is consistent with erasure.
  - (b) Skip the type with a warning.
  - (c) Map to a Dart 3 `sealed class` (deferred).
- **OD-11 — MS0032 strictness.** Two options:
  - (a) Error. This is *recommended*: it keeps C# dispatch honest.
  - (b) Warning with first-arm-wins semantics.
- **OD-12 — Sequencing with in-flight PRs.**
  - FR-019 depends on PR #298 (`IrRuntimeTypeTest`). The union stage starts after it merges. *Recommended*: hard dependency.
  - The FR-021 statement-label fix could instead land first as its own `fix:` PR.

## Assumptions

- The SDK 11 RC behaviour measured on `11.0.100-rc.1.26425.128` (Roslyn 5.9.0, STJ 11.0.0-rc.1) holds until GA. Any later change is contained by NFR-002.
- Consumers that write unions already use `LangVersion preview` and SDK 11. Metano does not polyfill `IUnion` for net10 projects.
- The released tool's host framework does not limit parsing (Roslyn does). Project evaluation runs in Roslyn's BuildHost child process, which rolls forward to the newest runtime (research R3).

## Dependencies

- PR #298 (#279): the `IrRuntimeTypeTest` classification (FR-019, NFR-004).
- #268 (polymorphic JSON): `[JsonPolymorphic]` union cases (OD-7).
- The non-object `JsonSerializerContext` root fix (batch-3 `json-collection-root`): union roots (OD-8).
- Existing `[GenerateGuard]` machinery: `[PlainObject]` / interface case tests.
