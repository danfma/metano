# Feature Specification: C# 15 union declarations

**Feature Branch**: `spec/009-csharp-unions` (spec directory `009-csharp-unions`)

**Created**: 2026-10-31

**Status**: Draft

**Input**: GitHub issue [danfma/metano#304](https://github.com/danfma/metano/issues/304), "Support C# 15 union declarations". Research notes from the video-studio Phase 4 investigation cover these sections: `stj-union`, `roslyn-union-api`, `host-net11`, `pattern-lowering-map`, the `csharp-unions` design and its critic. They are summarized with evidence tags in [research.md](./research.md).

**Traceability**: New capability, the spec change request for #304. It extends three legacy requirement families:
- semantic transformation, `FR-005`–`FR-014`, specifically pattern matching (`FR-013`, cited the same way by #279 / PR #298);
- serialization, `FR-032`–`FR-034`;
- diagnostics, `FR-038`–`FR-040`.

It also adds rows to the baseline [feature-support matrix](../001-project-baseline-evolution/baseline/feature-support-matrix.md) and reserves `MS0032`–`MS0037` in the [diagnostic catalog](../001-project-baseline-evolution/baseline/diagnostic-catalog.md). As in features 002–008, the FR identifiers below are local to this feature and are cited as "009 FR-0NN". FR-030–FR-038 were added during spec review; each sits next to the requirement it extends, so the list is grouped by topic, not by number.

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

  The decision comes from the single runtime-type-test classification that PR #298 introduces, never from a second, union-specific rule. This spec depends on the *capability* (a per-type runtime-test category plus its rendered expression), not on the class and member names PR #298 happens to use today (`IrRuntimeTypeTest.Of`, `Instanceof` / `Typeof(IrPrimitive)` / `Guard` / `None`). The goldens that show a primitive test are finalized after #298 merges (FR-019).
- **D3 — Exhaustive switch ends with a `never` check.** A switch over a union that tests every case ends with `$s satisfies never;`, followed by the existing non-exhaustive throw. A missing case therefore fails the TypeScript build, and `default(U)` still fails at runtime, as it does in C#.
- **D4 — Identity conversions.** The implicit case→union conversion and an explicit `(U)x` cast emit nothing. `u.Value` and `new U(x)` emit their operand unchanged. This is sound only because the compiler generates those members, so v1 accepts only compiler-generated unions (FR-005, FR-012).
- **D5 — JSON mirrors .NET 11 STJ strictly.**
  - A value serializes as its bare case payload: no wrapper, no discriminator.
  - Deserialization picks the case by JSON token kind only.
  - When two or more cases share a token kind (for example two object-shaped records), the build reports a diagnostic, the analogue of STJ's SYSLIB1227, and reading such a value fails at runtime with STJ's message.
- **D6 — Host stays net10.** The CLI tools keep `net10.0` and gain `<RollForward>Major</RollForward>`, so they also run on machines that have only .NET 11 installed. There is no multi-targeting and no `dotnet run --framework` change.
- **D7 — Repo SDK moves to 11 RC.** The repo `global.json` pins the SDK 11 RC with `allowPrerelease`. Union tests and the union sample compile against the net11 reference assemblies (`IUnion` / `UnionAttribute` live in `System.Runtime` 11). CI installs the exact SDK from `global.json` plus the 10.0.x runtime (FR-004).
- **D8 — Roslyn 5.9.0.** `Microsoft.CodeAnalysis.*` moves from 5.3.0 to 5.9.0.
  - `ITypeSymbol.IsUnion` and `Conversion.IsUnion` are experimental (`RSEXPERIMENTAL006`).
  - The public API exposes no case list, so cases are read from the union's public one-parameter instance constructors. A source union rejects user-declared ones with CS9374, which makes this reliable.
- **D9 — Diagnostic codes.** MS0029 and MS0030 are taken by spec 008 and MS0031 by #278 (`ExplicitImplementationConflict`). This feature takes the next free block, **`MS0032`–`MS0037`**.

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
8. **Given** `[GenerateGuard]` on a union, **When** transpiled, **Then** `isClip(v): v is Clip` is emitted as the OR of the case tests, in the union's own file, with value imports for every case class and guard it tests (FR-017, FR-035).
9. **Given** a pattern switch statement with a conditional `if (x) break;` in the middle of a section, a `default:` section that is not last, a section that mixes `case 1:` with `case string s:`, or a switch nested inside another switch or inside a `for` loop, **When** transpiled, **Then** the output follows the labeled-block lowering of FR-021: every switch-targeting `break` leaves the switch (never the enclosing loop), and `default` runs only after every other label fails.
10. **Given** `id switch { > 5 => "big", string s => s, int => "small" }` over `Id(int, string)` holding `"10"`, **When** run in TS, **Then** the result is `"10"`, as in C# (probe: the relational arm does not match a string). The relational arm is emitted as `typeof $s === "number" && $s > 5` (FR-033).
11. **Given** overloads `F(Clip c)` / `F(string s)`, or a constructor overloaded on `Id` and `bool`, **When** transpiled, **Then** the dispatcher tests a `Clip` argument as the OR of its case tests, and never as `instanceof Clip` or `typeof === "object"` (FR-032).
12. **Given** `((IUnion)u).Value`, or `t.Value` inside `T Read<T>(T t) where T : IUnion`, **When** transpiled, **Then** the result follows FR-034, and the output never contains `.value` on a union value.

### User Story 3 - JSON round-trips byte-for-byte with .NET 11 STJ (Priority: P2)

A service written in .NET 11 serializes a `Holder(Clip Clip, Id Id)` with STJ, and the TypeScript client generated by Metano deserializes it. The client serializes it back, and the bytes equal what .NET wrote.

**Why this priority**: Wire parity is the reason most consumers share types between C# and TS. It comes after US1/US2 because it builds on the case tests.

**Independent Test**: Use fixtures captured from a .NET 11 STJ probe (`{"Clip":{"Seconds":3},"Id":"abc"}`, `5`, `"x"`, `null`, a list, a map). The generated context deserializes each fixture, and serializing the result reproduces the fixture byte-for-byte. Ambiguous and unsupported inputs throw errors whose messages match STJ's.

**Acceptance Scenarios**:

1. **Given** `Id` and the value `5`, **When** serialized, **Then** the output is `5`; for `"x"` it is `"x"`; for `null` it is `null`.
2. **Given** `Holder(new SilenceClip(3), "abc")`, **When** serialized, **Then** the output is `{"Clip":{"Seconds":3},"Id":"abc"}`, with no wrapper and no discriminator.
3. **Given** `Id` and the input `5.5`, `true` or `{}`, **When** deserialized, **Then** the error message matches STJ's (see [contracts/json-wire.md](./contracts/json-wire.md)).
4. **Given** `Clip(ImageClip, SilenceClip)` reachable from a `JsonSerializerContext`, **When** transpiled, **Then** `MS0036` (warning) names the union and the shared token kind `Object`. At runtime, deserializing any object for `Clip` throws `JSON value type 'Object' is ambiguous for union type 'Clip' …`, exactly as STJ does. Serialization still works.
5. **Given** a union inside `List<T>`, `Dictionary<string, T>`, `T?`, or a `[PlainObject]` DTO field, **When** round-tripped, **Then** the union descriptor composes with the container descriptor and matches STJ.
6. **Given** a context whose **only** root is `Holder(Clip Clip, Id Id)`, **When** transpiled, **Then** the context still declares the `imageClip` and `silenceClip` spec getters (the closure reaches union cases, FR-030), declares **no** spec for `Clip` itself, and `tsgo --strict` accepts it.
7. **Given** a context that reaches `Opt<int>` (`union Opt<T>(T, None)`), **When** transpiled, **Then** the union descriptor is built for the constructed instance (cases `int`, `None`), the `none` spec getter exists, and the descriptor's STJ name is the one STJ prints for that instance (FR-030, FR-037).
8. **Given** `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]` or `NumberHandling = AllowReadingFromString` on the context, and `Id(int, string)`, **When** `"5"` is deserialized, **Then** it throws `JSON value type 'String' is ambiguous for union type 'Id' …`, as STJ does (probe R10), and MS0036 reports the shared `String` kind (FR-037).
9. **Given** `Dictionary<Id, int>` reachable from a context, **When** transpiled, **Then** MS0032 reports that a union cannot be a dictionary key. STJ itself throws `NotSupportedException` for it (probe R10).

### User Story 4 - Unions cross package boundaries (Priority: P2)

A union declared in an `[EmitPackage]` library is used by a consumer project. The consumer imports the union as a type and imports the case classes as values wherever it tests them.

**Independent Test**: `TranspileWithLibrary(lib with Clip + cases, consumer with Describe(Clip))`. The consumer has `import type { Clip } from "lib/…"`, value imports for `ImageClip` and `SilenceClip`, and the library dependency in `package.json`.

**Acceptance Scenarios**:

1. **Given** a library union read from metadata (constructors marked `[CompilerGenerated]`, `IsImplicitlyDeclared = false`), **When** the consumer is transpiled, **Then** the union semantics and cases are identical to the in-source case.
2. **Given** a library `[PlainObject]` case with `[GenerateGuard]`, **When** the consumer tests it, **Then** `isX` is imported from the library package (this closes today's local-only guard-import gap).
3. **Given** a consumer that uses a library union without `LangVersion preview`, **When** transpiled, **Then** Roslyn's CS8652 surfaces through MS0009 with the C# message unchanged.
4. **Given** `TranspileWithLibrary` where the library declares `Id(int, string)` and `Clip(ImageClip, SilenceClip)` with their case records, and the **consumer** declares a `JsonSerializerContext` rooted at `Holder(Clip, Id)`, **When** the consumer is transpiled, **Then** `Id` (primitive cases only) gets a working inline union descriptor, and the `Clip` property gets MS0032 naming the cross-assembly case `ImageClip`. The consumer context never references a `this.imageClip` getter it does not declare (FR-038, OD-13).

### User Story 5 - Shapes that erasure cannot represent faithfully are rejected (Priority: P2)

A developer writes a union whose C# meaning cannot survive erasure, for example `union N(int, double)`, where both cases are JS `number`. The build fails with a specific diagnostic instead of producing TypeScript that dispatches differently from C#.

**Acceptance Scenarios**:

1. **Given** `union N(int, double)`, `union S(IShape, Square)` (Square implements IShape), `union E(object, string)`, two guarded `[PlainObject]` cases (same shape or one shape a subset of the other), a guarded `[PlainObject]` case next to a record case, or two array-shaped cases, **When** transpiled, **Then** `MS0033` (error) names the two cases and their shared erased domain (FR-013).
2. **Given** a union with body members, an extra interface, a union-typed case, or `[PlainObject]` / `[Branded]` / `[InlineWrapper]` / `[StringEnum]` / `[ExportedAsModule]` / `[JsTuple]` on the union, **When** transpiled, **Then** `MS0032` (error) states the reason.
3. **Given** a hand-written `[Union] class HU : IUnion` (or `[Union] struct`) whose case constructors are user code, **When** transpiled, **Then** `MS0032` (error) states that only compiler-generated unions are supported. The probe in R10 shows why: `HU h = 3` stores `6` because the user constructor doubles its input, and the identity rewrite would drop that logic.
4. **Given** an interface or `[PlainObject]` case without `[GenerateGuard]`, or a case type with no runtime test, **When** a test against it is needed, **Then** `MS0034` (error) is reported.
5. **Given** `default(Clip)` or `default` in a `Clip` position, **When** transpiled, **Then** `MS0035` is reported (severity is an open decision, OD-3).
6. **Given** `clip.ToString()`, `$"{clip}"`, `clip.GetType()`, `o is Clip` where `o` is `object`, or `id.Equals(5)` where the argument is not statically an `Id`, **When** transpiled, **Then** `MS0037` (warning) states that the erased output differs from C#.
7. **Given** a reference to `Opt<Clip>` (a union reached through a type argument), `G<string>` for `union G<T>(T, string)`, or `Opt<double>` for `union Opt<T>(T, int)`, **When** transpiled, **Then** MS0032 (reason: union-typed case) or MS0033 (overlap) is reported once per constructed instance, at the first reference site. `Opt<int>` for `union Opt<T>(T, None)` is accepted (FR-031).

### User Story 6 - Dart does not crash on unions (Priority: P3)

**Acceptance Scenarios**:

1. **Given** a union in a Dart run, **When** transpiled, **Then** the output contains `/// union: ImageClip | SilenceClip` and `typedef Clip = Object;` (`Object?` if any case is nullable). There is no MS0009 and no exception.
2. **Given** a switch expression over a union, **When** transpiled for Dart, **Then** it ends with `_ => throw StateError('Non-exhaustive switch expression')`. Pattern labels in statements are lowered and never printed as `default:`.
3. **Given** `union Opt<T>(T, None)`, **When** transpiled for Dart, **Then** the output is `typedef Opt<T> = Object;` with the `/// union: T | None` comment.
4. **Given** a pattern switch statement with labels and a mid-section `break`, and a switch expression over `Clip?` with and without a `null` arm, **When** transpiled for Dart, **Then** the output matches the Dart tests of FR-027: a native Dart 3 pattern `switch` with `default:` last, and a nullable scrutinee keeps the `_ => throw` tail.

### Edge Cases

- **`default(U)` / zero-initialized unions.** In C#, `Value == null`. An exhaustive switch over it throws `SwitchExpressionException`, and the TS throw tail reproduces this. `default` in a non-null TS position does not type-check under strict mode, so see OD-3.
- **Default values MS0035 does not catch (known erasure divergences, not diagnosed in v1).** All of these yield a C# `default(U)` that erases to `null` or `undefined` in TS:
  - elements of `new Clip[n]` (probe R10: `arr[0].Value is null`). JS array holes and `new Array(n)` elements are `undefined`, which is not `=== null`, so the `null =>` arm does not catch them, while the throw tail still does;
  - `FirstOrDefault()`, `SingleOrDefault()`, `TryGetValue(out Clip)` on a miss, `GetValueOrDefault()`;
  - generic `default(T)` with `T = Clip`;
  - `Clip?` holding `default(Clip)`: in C# `HasValue == true` (probe R10), but the erased value is `null`, so a TS `!== null` test reads it as "no value". `Nullable<U>.HasValue` is reported by MS0037 (FR-016); a plain `!= null` is not, because it is too common to warn on.
- **Union as a dictionary key.** STJ throws `NotSupportedException` ("not a supported dictionary key") for `Dictionary<Id, V>` in both directions (probe R10), so a JSON-reachable union key gets MS0032. Outside JSON, `Dictionary<Clip, V>` maps to a JS `Map`, which compares record cases by reference identity. That divergence already exists for record keys and is not union-specific.
- **`Nullable<U>.Value` vs `U.Value`.** On a `Clip?`, `x.Value` is `Nullable<Clip>.Value`, which returns a `Clip`. The identity rewrite in D4 MUST apply only to the union's own `Value` property symbol. On a `Clip?`, `x?.Value` reads the union's `Value` through a conditional access and rewrites to `x`.
- **Boxing.** `object o = clip; o is ImageClip` is **false** in C#, because the box holds a `Clip`, but **true** after erasure. Conversely, `object o = new ImageClip(…); o is Clip` is false in C# but true in TS. Both are reported as MS0037 divergences (OD-6 decides whether v1 detects the boxing conversion itself).
- **Value semantics.** `u1.Equals(u2)` and `u.GetHashCode()` compare `Value` structurally in C# (`ValueType`). They lower to the runtime `equals(a, b)` and hash helpers, which handle primitives, never to `u.equals(…)`, which would crash on a `number`. `HashSet<Id>` and dictionary keys rely on this. `id.Equals(5)` is **false** in C# (the boxed `int` is not an `Id`; probe R10) but `equals(id, 5)` is true in TS, so a non-`U` argument gets MS0037. A record with a `Clip` field synthesizes `equals` / `hashCode` through the same runtime helpers (the `IrEqualityClassifier` non-strict path); a golden covers it.
- **Relational patterns over a union.** C# applies a relational pattern on a union input to `Value` with an implicit type test, as for `object`: `id is > 5` on an `Id` holding `"10"` is false (probe R10). JS `"10" > 5` is true. FR-033 prefixes the case test. Constant patterns need no prefix, because they lower to `===`, which already distinguishes `5` from `"5"`.
- **Access through `IUnion`.** `((IUnion)u).Value`, `t.Value` under `where T : IUnion`, and `o is IUnion` all work in C# (probe R10) and bind to `IUnion.Value`, not to the union's own `Value`. FR-034 decides what Metano does.
- **Nested unions.** For `union Outer(Inner, A)` with `union Inner(int, string)`, C# keeps `Inner` boxed (`o is int` is CS0184, never true), and STJ writes `5` for `Outer(Inner 5)` but cannot read `5` back. Flattening would change C# semantics, so v1 rejects union-typed cases with MS0032.
- **Order of cases** follows declaration order (constructor member order). The TS alias, the dispatch and the JSON write all use it.
- **A union nested in another type.** `class Outer { public union U(A, B); }` goes through `TransformNestedTypes` → `BuildTypeStatements` into the `namespace Outer` companion. The alias is emitted as `export type U = A | B;` inside that namespace, and its case imports must be collected on the namespace path, the same path where spec 006 found import gaps. A golden covers it (FR-028).
- **Generic unions.** The alias is always emitted. Pattern tests or JSON against an open type-parameter case raise MS0034 (the negation-of-siblings trick is deferred). A test against the *substituted* case of a closed instance (`Opt<int>` with `int n =>`) is supported, because the case is no longer open. Constructed instances are validated like declarations (FR-031). Probe R10: `G<string>` for `union G<T>(T, string)` is a legal type reference, but converting a `string` to it is CS0457 (two identical conversions), so only `default` can produce one.
- **Hand-written `[Union] struct/class : IUnion`.** Roslyn reports `IsUnion = true`, and detection runs before the `TypeKind` dispatch, so a `[Union] class` is covered too. v1 rejects every hand-written union with MS0032 (FR-012): its constructors and `Value` are user code that can normalize or compute (probe R10), and a `[Union] class` value can itself be `null`. A plain `struct : IUnion` without `[Union]` reports `IsUnion = false` and stays an ordinary struct.
- **A union case that is a `[JsonPolymorphic]` base.** STJ handles it with a `$type` discriminator. The TS side depends on #268 (polymorphic JSON), so until then it gets MS0032 in a JSON-reachable position (OD-7).
- **`[JsonUnion(TypeClassifier = …)]`** and `JsonUnionTypeStructuralClassifier`. A classifier is C# code over `Utf8JsonReader` and cannot be transpiled, so it gets MS0032 when JSON-reachable.
- **Union as a `[JsonSerializable(typeof(U))]` root.** This depends on the non-object root fix (`json-collection-root`, `ValueSpec`). It is out of scope until that lands (OD-8).
- **Experimental API drift.** RSEXPERIMENTAL006 APIs and STJ RC messages may change before GA. Only one core file reads the experimental API, and the STJ messages live in one runtime module.

## Requirements *(mandatory)*

### Functional Requirements

Toolchain:

- **FR-001**: `Microsoft.CodeAnalysis.CSharp`, `.CSharp.Workspaces` and `.Workspaces.MSBuild` MUST be upgraded to 5.9.0. Outside the union work, the existing suite and every `targets/` output MUST be unchanged by the upgrade.
- **FR-002**: The CLI tool projects (`Metano.Compiler.TypeScript`, `Metano.Compiler.Dart`) MUST keep `net10.0` as their only target framework and MUST set `<RollForward>Major</RollForward>`, so the installed tool starts on a machine where only a newer runtime (.NET 11) is installed.
- **FR-003**: The repo `global.json` MUST pin the SDK 11 RC with `allowPrerelease: true`. Every existing net10.0 sample, test and package MUST build and produce byte-identical `targets/` output under it.
- **FR-004**: CI (`ci.yml`) and release (`release.yml`) MUST install the **exact** SDK pinned in `global.json` (`actions/setup-dotnet` with `global-json-file: global.json`) plus `10.0.x`. They MUST NOT float to `11.0.x`, so a newer RC or GA cannot change the build or the fixtures without a reviewed `global.json` bump. The 10 runtime stays because the tests and the CSharpier local tool run on it.
  - The STJ fixture-drift check (OD-2a) MUST run as a separate job that runs only when `global.json`, the fixture generator or the fixtures change, so STJ drift never fails an unrelated PR.
  - The `sample-unions` negative type-check script (expects TS1360, plan.md) MUST be an explicit CI step.
  - Release builds pack the `net10.0` tools with the pinned RC SDK; OD-14 records this choice.

Detection and IR (core, target-agnostic):

- **FR-005**: The core MUST detect a union only through `ITypeSymbol.IsUnion`, in one adapter file that is the sole place allowed to use `RSEXPERIMENTAL006` APIs. Detection MUST run before any `TypeKind` dispatch, in both the TypeScript and Dart transformers. A detected union is *supported* only when it is compiler-generated:
  - a source union declared with `UnionDeclarationSyntax` (the `union` keyword), or
  - a metadata union whose case constructors all carry `[CompilerGenerated]` (research R2).

  Every other `IsUnion` type (a hand-written `[Union] struct` / `[Union] class : IUnion`, from source or metadata) gets MS0032 and is not transpiled (FR-012).
- **FR-006**: Union cases MUST be read as the parameter types of the union's public, non-static, single-parameter instance constructors, in member (declaration) order. This MUST work identically for source and metadata symbols and for generic unions.
- **FR-007**: The IR MUST represent a union declaration as its own declaration kind (`IrUnionDeclaration`: name, visibility, ordered case type refs, type parameters, attributes). A union reference MUST carry a `Union` semantic kind with its cases. This is a language-level construct, not a BCL-specific node.
- **FR-008**: The IR switch expression MUST carry the scrutinee type, with its nullability. Switch-statement sections MUST carry their pattern labels, each with an optional `when` clause, next to their constant labels, so `case T x:` labels are represented rather than dropped and a section can mix both kinds. A `break` statement MUST record whether it targets the enclosing switch (FR-021).
- **FR-009**: The core MUST rewrite these identities once, for every target:
  - the union `Value` property read (`u.Value`, `u?.Value`) becomes the receiver;
  - `new U(x)` becomes `x`;
  - the implicit and explicit case→union conversions emit nothing.
- **FR-010**: Union-typed values MUST lower `Equals` / `GetHashCode` to the runtime equality and hash helpers that accept primitives. This core node is the one every target renders; the Dart target follows it too (FR-027). Synthesized `equals` / `hashCode` of a record or class with a union-typed field MUST use the same helpers for that field.
- **FR-011**: The runtime-requirement scanner MUST walk type patterns (expressions and statements) and union declarations, so every runtime helper a case test needs (e.g. `isInt32`, a guard) is imported.
- **FR-032** *(added in review)*: Every producer of a runtime type check MUST render a union as the OR of its case tests, taken from the one classification (FR-019, NFR-004), and MUST NOT fall through to a default arm (`instanceof U`, `typeof === "object"`). The producers are:
  - pattern lowering (`is`, `as`, switch arms, statement labels);
  - the overload, constructor and operator dispatchers, which build their checks through `IrTypeCheckBuilder.GenerateForParam` today;
  - the scanner's `ResolveTypeCheckHelper`, which picks the runtime helper to import;
  - the JSON case tests (FR-023) and the union guard (FR-035).

  `IrTypeCheckBuilder` and `ResolveTypeCheckHelper` are existing second decision tables; this feature routes their union arm through the shared classification, and plan.md lists them in the `IrNamedTypeKind` audit. Goldens cover an overloaded method `F(Clip)` / `F(string)` and a constructor overloaded on `Id`.
- **FR-033** *(added in review)*: A relational pattern (`> 5`, `<= 'z'`) whose input is a union MUST be emitted as the case test of the pattern's narrowed type joined with the comparison (`typeof $s === "number" && $s > 5`). The narrowed type comes from Roslyn's `NarrowedType` on the pattern operation ([I]: verified for type patterns, to be confirmed for relational patterns with a Roslyn probe in stage 009c). The same rule applies to an `object` input, whose output today is a bare `$s > 5`; that change is the second `fix:` exception to NFR-001. Constant patterns are unchanged (they lower to `===`).
- **FR-034** *(added in review)*: Access to a union through `IUnion` MUST NOT emit `.value`:
  - `((IUnion)u).Value` where the cast operand is statically a union MUST be rewritten like `u.Value` (FR-009);
  - a union converted to `IUnion` in any other way (assignment, argument, `o is IUnion`), or used as a type argument for a type parameter constrained to `IUnion`, MUST get MS0032 (reason: `IUnion` access), because the erased value has no `Value` member and `IUnion` has no TS mapping.

  Tests cover the cast and the generic-constraint case.

Validation (core; at the declaration once per type, and at use sites once per constructed instance):

- **FR-012**: `MS0032 UnsupportedUnionShape` (Error) MUST be reported in any of these cases:
  - the union declares body members or interfaces beyond `IUnion`;
  - a case is itself a union;
  - the union carries `[PlainObject]`, `[Branded]`, `[InlineWrapper]`, `[StringEnum]`, `[ExportedAsModule]` or `[JsTuple]`;
  - a JSON-reachable union uses `[JsonUnion(TypeClassifier = …)]`, or has a `[JsonPolymorphic]` case before #268 lands;
  - the union is hand-written (`[Union]` + `IUnion` without `union` syntax, or from metadata without `[CompilerGenerated]` case constructors; FR-005);
  - the union is accessed through `IUnion` other than by the `Value` cast of FR-034;
  - a JSON-reachable position uses the union as a dictionary key, or a JSON-reachable union has a case whose STJ wire form Metano does not reproduce (FR-037) or that the context cannot describe (FR-030, FR-038).

  `[Name]`, `[EmitInFile]`, `[NoEmit]`, `[Ignore]`, `[Transpile]` and `[GenerateGuard]` MUST be supported.
- **FR-013**: `MS0033 IndistinguishableUnionCases` (Error) MUST be reported for every pair of cases whose **erased JS value domains overlap**, regardless of whether their test expressions differ. Each case maps to one domain category:
  - `number` (every numeric primitive, numeric enums, and `[Branded]` over a number), `string` (`string`, `char`, `Guid`, `[StringEnum]`, `[Branded]` over a string), `boolean`, `bigint`;
  - `function` (delegates; a delegate case is already MS0034, because the classification has no test for it);
  - `array` (every type that erases to a JS array: `T[]`, `List<T>` and the other list-like types; a set that erases to the runtime `HashSet` class is `instanceof`);
  - `instanceof C` (a transpilable class or record, a temporal type, `decimal`, an `[Import]` / `[External]` class);
  - `structural` (an interface or `[PlainObject]` case, tested by its guard).

  Two cases overlap when:
  1. one converts implicitly to the other (Roslyn conversion classification): `union S(IShape, Square)`, `union E(object, string)`;
  2. both are in the same primitive category: `int` / `double`, `int` / `long`, `char` / `string`, `Guid` / `string`, a `[Branded]` string / `string`, a numeric enum / `int`;
  3. one is `instanceof C` and the other is `instanceof` a subclass of `C`;
  4. both are `structural`, or one is `structural` and the other is `instanceof C`. A structural guard accepts any object with the required members, so a `SilenceClip{Seconds}` guard returns true for an `ImageClip{Path, Seconds}` value and case order would replace C#'s nominal dispatch;
  5. both are in `array`.

  Tests cover `int` / `double` (whatever primitive rendering #298 ships: test identity no longer matters), two same-shape guarded `[PlainObject]` cases, a subset-shape pair, `[PlainObject]` next to a record, and two array cases. OD-15 records whether rule 4 may be relaxed later.
- **FR-014**: `MS0034 UntestableUnionCase` (Error) MUST be reported when a case test is required and the case has no runtime test:
  - an interface or `[PlainObject]` without `[GenerateGuard]`;
  - a type parameter that is still open at the test site;
  - a delegate (the classification has no test for it);
  - a sequence typed as TS `Iterable<T>` (`IEnumerable<T>`, `IReadOnlyCollection<T>`; since #129), which has no reliable runtime test;
  - a non-transpilable class without `[Import]` / `[External]`.

  A single `array` case is testable with `Array.isArray`; only a second array case is an error, and that error is MS0033 (FR-013), not MS0034. For non-generic unions the check runs at the declaration. For a type-parameter case it runs at the pattern or JSON use site (OD-4), and a closed instance whose substituted case is testable (`Opt<int>` with `int n =>`) passes.
- **FR-015**: `MS0035 UnionDefaultValue` MUST be reported for `default(U)`, a target-typed `default` in a union position, and a non-nullable union field or auto-property that no initializer and no constructor assigns. Emission and severity follow OD-3.
- **FR-016**: `MS0037 UnionErasureDivergence` (Warning) MUST be reported in these cases, and the output MUST still be emitted:
  - `ToString()` on a union;
  - string interpolation of a union;
  - `GetType()` on a union;
  - `is U` / `as U` / a `U` type pattern over an input whose static type is not `U` or `U?`;
  - `u.Equals(x)` where `x` is not statically `U` or `U?` (C# `ValueType.Equals` first checks that the boxed argument is a `U`, so `id.Equals(5)` is false; probe R10);
  - `HasValue` on a `Nullable<U>` (a `Clip?` holding `default(Clip)` has `HasValue == true` in C# but erases to `null`).

  The SC-002 runtime check of the `default` throw therefore injects the value from TS (`null as unknown as Clip`), because the sample's C# cannot write `default(Clip)` without MS0035.
- **FR-031** *(added in review)*: MS0032 (reason: union-typed case) and MS0033 MUST also run on every **constructed** `INamedTypeSymbol` of a generic union that appears in a type reference, a pattern, or a JSON-reachable position, after type-argument substitution. Each constructed instance is checked once per compilation, keyed on `SymbolEqualityComparer.Default`, and the diagnostic is reported at the first reference site in source order (NFR-005). MS0034 at a use site fires only for a case that is still an open type parameter there. Tests:
  - `Opt<Clip>` for `union Opt<T>(T, None)` → MS0032 (a union reached through a type argument; probe R10: STJ writes `5` for `GOpt<Id>` holding `5` and cannot read it back);
  - `G<string>` for `union G<T>(T, string)` → MS0033;
  - `Opt<double>` for `union Opt<T>(T, int)` → MS0033;
  - `Opt<int>` for `union Opt<T>(T, None)` → accepted, and `o switch { int n => …, None => … }` lowers with `typeof`.

TypeScript target:

- **FR-017**: A union declaration MUST emit `export type <Name><TypeParams> = <Case₁> | … | <Caseₙ>;`. A nullable case contributes `| null` in its position. Case types MUST be imported with `import type` in the declaring file, **unless** the file also emits the union guard (FR-035). In that case, every case class the guard tests with `instanceof` and every case guard it calls are value imports, and the remaining case types stay type-only. A golden for `[GenerateGuard] union Clip` and `[GenerateGuard] union Id` checks the import lines.
- **FR-018**: Union type references MUST map to the alias name. `Nullable<U>` maps to `U | null`, using the existing nullable mapping.
- **FR-019**: Every runtime test against a union case MUST come from the single runtime-type-test classification that PR #298 (#279) introduces. This spec requires the **capability**, not specific names: for every case type the classification yields (1) a category (`instanceof`, primitive, guard, array, none) and (2) the rendered test expression. A test against the union itself (`o is U`, `[GenerateGuard]` on U) MUST be the OR of its case tests. Two consequences:
  - an `array` case renders as `Array.isArray(v)`. #298 currently classifies collections as untestable, so this feature adds the array category to that one classification rather than a union-local rule;
  - goldens that show a primitive test (`typeof $s === "number"` vs `isInt32($s)`) are finalized after #298 merges, using whatever rendering it ships. At the head of PR #298 on 2026-10-31, `int` and `double` classify as distinct `Typeof(Int32)` / `Typeof(Double)` results; FR-013 is written over value domains so that this difference cannot hide an overlap.
- **FR-020**: A switch **expression** MUST emit `$s satisfies never;` before the existing throw when all of these hold:
  - its scrutinee type is a union;
  - every arm before the catch-all is an unguarded type pattern on a distinct case, or a `null` constant;
  - there is no catch-all;
  - either the scrutinee and every case are non-nullable, or a `null` arm exists.

  Otherwise only the throw is emitted. The throw message stays `"Non-exhaustive switch expression"`, so existing goldens are unchanged.
- **FR-021**: A switch statement whose labels are **all constants** keeps today's native TS `switch` (NFR-001). A switch statement with at least one pattern label (`case T x:`, `case > 5:`, with or without `when`) MUST lower to a labeled block over a once-evaluated subject:

  ```ts
  $switch: {
    const $s = subject;
    if (test₁) { …; }
    else if (test₂) { …; break $switch; … }
    else { default body }
  }
  ```

  The rules:
  - **Tests.** A constant label tests `$s === c`, a pattern label tests its pattern, and `when g` joins as `test && g` (nested inside the binding block when `g` reads a designator, as switch-expression arms do). A section with several labels (`case A: case B b:`) tests the OR of its label tests. It binds no designators, because C# leaves a designator of a multi-label section unassigned (CS0165), so the body cannot read it.
  - **`default` goes last.** The `default:` section becomes the final `else`, wherever it appears in the source. In C# it runs only after every other label fails, and C# forbids fall-through, so moving it does not change behaviour.
  - **`break`.** A `break` whose target is this switch (Roslyn `IBranchOperation` target) becomes `break $switch`, except a trailing unconditional `break` at the end of a section, which is dropped. A `break` that targets a loop nested inside a section stays a plain `break`. A `continue` that targets an enclosing loop stays a `continue`, which is valid inside the labeled block.
  - **Labels.** The block is labeled only when at least one `break $switch` remains; otherwise it is a plain block, as in the contract example. Nested pattern switches use `$switch`, `$switch1`, `$switch2`, … by nesting depth, because a JS label cannot be redeclared inside itself. The subject const follows the same scheme (`$s`, `$s1`, …) only when an inner subject expression reads the outer `$s`; otherwise block scoping already isolates it.
  - **`goto case` / `goto default`.** These are reported as `MS0001 UnsupportedFeature` in a switch that takes this lowering. In an all-constant switch they keep today's behaviour.
  - **No exhaustiveness.** The lowering has no `never` check and no throw, because C# statement switches are not exhaustive.

  This also fixes today's silent drop of `case T x:` labels in non-union code. Goldens (TS and Dart, FR-027) cover a mid-section conditional `break`, a pattern switch inside a `for` loop, `default` in a middle section, mixed constant and pattern labels, a multi-label section, and nested pattern switches.
- **FR-022**: Cross-package unions MUST resolve semantics and cases from metadata (FR-006). The alias is imported as a type, case classes used in `instanceof` as values, and guards from the producing package. This extends guard-import resolution, which today is local-only, to the cross-assembly origin map.
- **FR-035** *(added in review)*: The union guard (`[GenerateGuard]` on a union) MUST be produced by the IR-based union bridge (`IrToTsUnionBridge`), as the OR of the case tests of FR-019. `TypeGuardBuilder`, the Roslyn-based guard builder used for records, classes and `[PlainObject]` types, MUST skip unions; otherwise it would emit a struct shape guard that checks a `value` field. A guard validates `unknown` input, so for an integer primitive case it MUST use the classification's validating test (the runtime `isInt32` family), not a bare `typeof === "number"`: `isId(5.5)` returns `false`, matching C#, where `5.5` is not an `int`. The golden checks the `isInt32` call and its runtime import.

JSON (TypeScript target + runtime):

- **FR-023**: The generated `SerializerContext` MUST describe a union-typed value with a `union` `TypeDescriptor` that carries the union name and, per case in declaration order:
  - the case's JSON token kind;
  - its runtime test;
  - a thunk to the case's descriptor.

  The descriptor MUST compose inside `array`, `map`, `hashSet` and `nullable` descriptors and in `[PlainObject]` DTO fields. It is built **inline** at each use, never as an object spec, so a union never gets a context getter of its own.
- **FR-030** *(added in review)*: The context's contract closure MUST reach union cases. Today `JsonSerializerContextTransformer.ClassifyPropertyType` returns `Ref` for a union (a transpilable `TypeKind.Struct`), so `ReachableObjectTypes` queues the union struct itself as an object spec (with a `value` property) and never queues its cases (`JsonSerializerContextTransformer.cs:270-313`). The fix:
  1. `ClassifyPropertyType` gets a `Union` kind, checked through the core union reader **before** the nullable-inner, `Ref` and `Struct` checks, so a union never becomes an object spec;
  2. `ChildTypesOf(Union)` returns the union's cases (after type-argument substitution for a constructed generic union), so the closure queues every object-kind case and recurses into container cases;
  3. a constructed generic union (`Opt<int>`) is supported: its descriptor is built for that instance, and its non-generic cases are queued as usual;
  4. a case that the closure cannot describe gets MS0032 when JSON-reachable, naming the case and the reason: a closed generic object type (`Box<int>`; the closure skips generic types today, `IsGenericType: false`) or an object type from another assembly (`IsInCurrentAssembly`; FR-038). Primitive, temporal, enum and branded cases need no spec and are always describable.

  A TUnit test uses `Holder(Clip, Id)` as the **only** root and asserts that the context declares the `imageClip` and `silenceClip` getters and no `clip` getter; `sample-unions` has the same scenario, compiled by tsgo.
- **FR-024**: Serialization MUST write `null` for `null`. Otherwise it MUST write the bare payload of the first case whose runtime test matches, with no wrapper and no discriminator, using that case's descriptor.
- **FR-025**: Deserialization MUST classify by JSON token kind only.
  - `null` returns `null`.
  - Zero candidate cases throws `JSON token type '<Token>' is not supported for union type '<U>'.`
  - More than one candidate throws `JSON value type '<Kind>' is ambiguous for union type '<U>' because multiple case types can use this value type. Specify a custom type classifier to support deserialization.`
  - Exactly one candidate delegates to that case.

  Token and kind names follow STJ (`Number`, `String`, `True`, `False`, `StartObject`/`Object`, `StartArray`/`Array`); the exact table is in [contracts/json-wire.md](./contracts/json-wire.md). The union name in the messages is the name STJ prints: `Id` for a non-generic union and ``GOpt`1[Id]`` for a constructed generic one (probe R10; the format for several or nested type arguments is [I] and is pinned by a fixture).
- **FR-037** *(added in review)*: A case's JSON token kind MUST be the token **STJ** writes and reads for it under the context's options, not the token Metano's own descriptor happens to use. Where the two differ, the union is not reproducible and gets MS0032 when JSON-reachable (probe R10):
  - `byte[]`: STJ writes a base64 **string**; Metano's descriptor writes an array → MS0032;
  - an enum without a string-enum converter, including a `[StringEnum]` enum: STJ writes a **number**. Metano writes `[StringEnum]` as a string → MS0032 for a `[StringEnum]` case unless the enum or the context applies `JsonStringEnumConverter` (then its token is `String`);
  - `char`: STJ writes a string; Metano's context does not classify `char` today → MS0032;
  - `Uri`, `TimeSpan`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `Guid`: `String`, matching Metano.

  The transformer MUST also read `NumberHandling` from `[JsonSourceGenerationOptions]`, both as a named argument and through the `JsonSerializerDefaults.Web` constructor argument. Under `AllowReadingFromString` (part of the Web defaults), every number case is also a `String` candidate, so `Id(int, string)` reading `"5"` (or any string) throws STJ's `JSON value type 'String' is ambiguous …` message, and MS0036 reports the `String` overlap. `WriteAsString` and `AllowNamedFloatingPointLiterals` on a context that reaches a union with a number case get MS0032. Reading the Web defaults' naming policy is a pre-existing gap and out of scope here. `DeserializeAsync` behaves exactly like `Deserialize` (probe R10), and the TS runtime has a single entry point, so it needs nothing extra.
- **FR-038** *(added in review)*: Cross-package JSON. When the context lives in a consumer and the union (or its cases) in an `[EmitPackage]` library:
  - a union whose cases need no object spec (primitive, temporal, enum, branded) gets its inline descriptor in the consumer, with the case tests and guards imported through the origin-resolved imports of FR-022;
  - an object-kind case from another assembly gets MS0032 in v1 (OD-13), because the closure does not build specs for metadata types (`IsInCurrentAssembly`), and a `this.<case>` thunk to a getter the context does not declare would not compile (TS2339).

  `TranspileWithLibrary` tests cover both outcomes (US4-4).
- **FR-026**: `MS0036 AmbiguousUnionJsonCases` (Warning) MUST be reported once per union that is reachable from a `JsonSerializerContext` and has two or more cases with the same JSON token kind. The message names the cases and the kind, mirroring SYSLIB1227. Generation continues.

Dart target:

- **FR-027**: Dart MUST emit `typedef <Name><TypeParams> = Object;` (`Object?` if any case is nullable), preceded by a `/// union: A | B` doc comment; a generic union keeps its type parameters (`typedef Opt<T> = Object;`). Unions MUST NOT raise MS0009 or throw in the Dart target. Further rules:
  - Switch expressions over a union MUST end with `_ => throw StateError('Non-exhaustive switch expression')`, also when the scrutinee is a nullable union with or without a `null` arm (Dart has no `never` check, so the tail is always present).
  - Pattern switch statements render as a native Dart 3 `switch` with pattern cases (`case ImageClip i when …:`). Dart 3 has no implicit fall-through, a `break` inside a case leaves the switch as in C#, and `default:` is moved last. Labels are never printed as `default:`. [I]: the Dart 3 semantics are from the language specification, not from a probe; the Dart tests are the gate.
  - `Equals` / `GetHashCode` over a union render the core node of FR-010 (Dart `==` / `hashCode` on the erased `Object`, which is already structural for records).
  - Tests cover the generic typedef, a statement-label switch with a mid-section `break`, and a nullable union scrutinee.

Tests and CI:

- **FR-028**: Tests MUST cover every acceptance scenario above:
  - golden files under `tests/Metano.Tests/Expected/` for TS emission;
  - one test per diagnostic reason (MS0032–MS0037);
  - source-vs-metadata union reading;
  - `TranspileWithLibrary` cross-package output;
  - Dart shape tests;
  - a golden for a union nested in a class (`class Outer { public union U(A, B); }`: the alias inside `namespace Outer`, with its case imports present);
  - goldens for a record with a `Clip` field (`equals` / `hashCode` through the runtime helpers) and for `HashSet<Id>`.

  Inline test compilations of union sources MUST reference the net11 reference assemblies (FR-036).
- **FR-036** *(added in review)*: Union test compilations MUST use a separate reference set, `TranspileHelper.Net11UnionReferences`, never mixed with `BaseReferences` (which comes from the running net10 runtime's TPA directory: `typeof(object).Assembly.Location`). Mixing them would put two `System.Runtime` versions and two core libraries in one compilation. The set:
  - holds the `Microsoft.NETCore.App.Ref` **net11** reference pack plus `Metano.dll` (the annotations), and nothing from the TPA;
  - resolves the pack deterministically: the SDK version pinned in `global.json` → `<dotnet root>/sdk/<version>/Microsoft.NETCoreSdk.BundledVersions.props` → `BundledNETCoreAppPackageVersion` → `<dotnet root>/packs/Microsoft.NETCore.App.Ref/<that version>/ref/net11.0/*.dll` ([V] on `11.0.100-rc.1.26425.128`: the props file names `11.0.0-rc.1.26425.128`, and `IUnion` lives in that pack's `System.Runtime.dll`). The dotnet root comes from `DOTNET_ROOT`, falling back to the directory of the `dotnet` on `PATH`;
  - fails with a clear message that names the missing path and the `global.json` version when the pack is absent. It never skips silently;
  - is used for **both** the library and the consumer compilation of `TranspileWithLibrary` when either side declares a union, so metadata-union tests reference one runtime.

  `Metano.dll` targets net10, so its references unify upward to the net11 pack. CS1701 / CS1702 unification warnings are expected and suppressed in the compilation options. One test asserts that a union compilation reports no CS0433 (duplicate type), CS0518 or CS0656 error.
- **FR-029**: A sample `samples/SampleUnions` (net11.0, `LangVersion preview`) and its consumer `targets/js/sample-unions` MUST prove:
  - with `bun run build` (tsgo strict), that the output compiles, including the `never` checks;
  - with `bun test`, dispatch, the `default` throw, and JSON byte parity against fixtures produced by real .NET 11 STJ.

  CI MUST run that consumer's build and tests explicitly.

### Non-Functional Requirements

- **NFR-001 (no churn)**: For sources without unions, generated output MUST be byte-identical to `main`. There are two exceptions, both fixes of output that was wrong, and both are classified as `fix:` in the release notes:
  - FR-021: switch statements with pattern labels, which today become `default:`;
  - FR-033: relational patterns over an `object` input, which today emit a bare comparison with no type test.

  All-constant switch statements keep their native TS form, so they do not change.
- **NFR-002 (containment)**: `RSEXPERIMENTAL006` MUST be suppressed in exactly one core file, with a file-local pragma and no project-wide `NoWarn`. STJ message texts MUST live in one runtime module.
- **NFR-003 (target-agnostic core)**: Detection, case reading, validation and the identity rewrites live in `Metano.Compiler`. The TS and Dart targets only render.
- **NFR-004 (single classification)**: No second runtime-type-test decision table is introduced. Pattern lowering, the dispatchers (`IrTypeCheckBuilder`), union guards, JSON case tests and the scanner (`ResolveTypeCheckHelper`) all consume the classification of FR-019 for union types (FR-032). The domain categories of FR-013 are derived from that same classification plus the type's TS mapping, not from a separate table.
- **NFR-005 (determinism)**: Case order, diagnostic order and the emitted output are stable across runs and independent of whether the union comes from source or from metadata.

### Key Entities

- **Union declaration**: a named, possibly generic, ordered set of case types. It has no members of its own in v1.
- **Union case**: one case type, with its runtime test classification and its JSON token kind.
- **Union semantics**: the `Union` kind plus cases, attached to every reference to the union, so backends decide without Roslyn.
- **Union descriptor (runtime)**: the JSON `TypeDescriptor` variant that implements STJ's token-kind classification.

## Success Criteria *(mandatory)*

- **SC-001**: The issue's `Clip` / `Id` example transpiles to TS that passes `tsgo --strict`, with 0 `/* TODO */`, 0 wrapper classes and 0 occurrences of `.value` on a union.
- **SC-002**: Removing one arm from a union switch in the generated TS makes `tsgo` fail (TS1360). A bun test that passes `null as unknown as Clip` (the erased `default(Clip)`, injected from TS because the C# sample cannot write `default(Clip)` without MS0035) to the generated `describe` sees it throw `Non-exhaustive switch expression`.
- **SC-003**: For 100% of the JSON fixtures produced by .NET 11 STJ, TS deserialize followed by serialize reproduces the fixture bytes, and every STJ error fixture yields the same message.
- **SC-004**: All existing goldens and `targets/` outputs are unchanged after the toolchain bump and the feature, except the reviewed FR-021 fix diff.
- **SC-005**: Each MS0032–MS0037 reason has a test that asserts its code, severity and location.

## Open decisions

Each item carries a recommendation. Only OD-14 touches the toolchain stage (FR-001–FR-004): stage 009a needs it for `release.yml`. The others block only the stage that implements them.

- **OD-1 — Test project framework.** The inline union compilations need net11 reference assemblies (D7). Two options:
  - (a) Keep `Metano.Tests` on `net10.0` and feed `TranspileHelper` the `Microsoft.NETCore.App.Ref` 11 pack for union tests, through the separate reference set of FR-036. This is *recommended*: no RC runtime is needed for the whole suite.
  - (b) Move `Metano.Tests` to `net11.0`.
- **OD-2 — JSON fixture provenance.** The fixtures are captured from real .NET 11 STJ. Two options:
  - (a) A small net11 console project regenerates them in CI, followed by a `git diff --exit-code` check, in a separate job that runs only when `global.json`, the generator or the fixtures change (FR-004). This is *recommended*: it catches STJ RC changes on an SDK bump without failing unrelated PRs.
  - (b) A documented manual script, with the fixtures checked in.
- **OD-3 — `default(U)`.** Three options:
  - (a) MS0035 **error** in v1. This is *recommended*: `null` does not type-check at non-null TS positions, and the project's value-object rule already treats `default` as invalid.
  - (b) Emit `null` plus a warning.
  - (c) Emit `null as unknown as U` plus a warning.
- **OD-4 — Where MS0034 fires.** For non-generic unions it fires at the declaration (*recommended*); for type-parameter cases it fires at the use site. The alternative is to fire always at the use site, which allows pass-through-only unions over untestable cases.
- **OD-5 — Body members and extra interfaces.** Two options:
  - (a) Reject them in v1 with MS0032. This is *recommended*.
  - (b) Lower them to a companion `namespace Clip { export function … (self: Clip) }` and rewrite the call sites.
- **OD-6 — Boxing detection for MS0037.** Two options:
  - (a) Warn only on the syntactic sites in FR-016 in v1. This is *recommended*.
  - (b) Also warn at every union→`object`/interface conversion (`Conversion.IsBoxing` on a union source).
- **OD-7 — `[JsonPolymorphic]` cases.** Two options:
  - (a) MS0032 until #268 ships, then support it. This is *recommended*.
  - (b) Make #268 a hard prerequisite for the JSON stage.
- **OD-8 — Union JSON roots.** Two options:
  - (a) Defer `[JsonSerializable(typeof(U))]` roots until the non-object root work (`ValueSpec`, batch-3 `json-collection-root`) lands. Union-typed properties and container elements are in scope now. This is *recommended*.
  - (b) Do the root work inside this feature.
- **OD-9 — Implicit guards.** Two options:
  - (a) Require an explicit `[GenerateGuard]` on `[PlainObject]` / interface cases (MS0034). This is *recommended*: it is explicit and keeps guard emission opt-in.
  - (b) Synthesize a guard for every such case.
- **OD-10 — Dart typedef.** Two options:
  - (a) `typedef Clip = Object;`. This is *recommended*: it is consistent with erasure.
  - (b) Skip the type with a warning.
  - (c) Map to a Dart 3 `sealed class` (deferred).
- **OD-11 — MS0033 strictness.** Two options:
  - (a) Error. This is *recommended*: it keeps C# dispatch honest.
  - (b) Warning with first-arm-wins semantics.
- **OD-12 — Sequencing with in-flight PRs.**
  - FR-019 depends on PR #298 (`IrRuntimeTypeTest`). The union stage starts after it merges. *Recommended*: hard dependency.
  - The FR-021 statement-label fix could instead land first as its own `fix:` PR.
- **OD-13 — Cross-assembly object cases in a consumer context** (FR-038). Today's closure builds no spec for any metadata type, union or not. Three options:
  - (a) MS0032 in v1, revisited with the general cross-assembly context fix. This is *recommended*: it is honest, small, and does not decide the general design inside a union feature.
  - (b) The consumer context regenerates specs for metadata case types, using origin-resolved imports for the classes.
  - (c) The consumer imports the case specs from the producer package's own context, which requires a cross-package context-composition contract that does not exist yet.
- **OD-14 — Release SDK** (FR-004). The tools stay `net10.0`. Two options:
  - (a) Release packs with the pinned 11 RC SDK from `global.json`, the same SDK CI tests with. This is *recommended*: one SDK for test and pack, and R4 measured byte-identical output.
  - (b) Release overrides `global.json` and packs with SDK 10. This keeps preview tooling out of shipped packages, at the cost of packing with an SDK that CI never tested.
- **OD-15 — Structural overlap rule** (FR-013 rule 4). Two options:
  - (a) Any two structural cases, or a structural case next to a class case, always overlap. This is *recommended* for v1: guards accept supersets, so only this rule is sound without shape analysis.
  - (b) Allow structural cases whose required members are provably disjoint (neither shape is a subset of the other, and no class case satisfies either), behind tests.
- **OD-16 — STJ wire divergences in case types** (FR-037). `byte[]`, `[StringEnum]` without a converter, and `char` are written differently by STJ and by Metano's descriptors, for unions and non-unions alike. Two options:
  - (a) MS0032 for such union cases in v1; the descriptor fixes are separate issues. This is *recommended*: it keeps the feature scoped, and the descriptor fix changes non-union output.
  - (b) Fix the three descriptors inside this feature, which breaks NFR-001 for existing contexts.
- **OD-17 — Scope of the `object`-input relational fix** (FR-033). Two options:
  - (a) Ship it in stage 009c as a `fix:` commit next to the FR-021 fix. This is *recommended*: it is the same code path, and the union rule needs it anyway.
  - (b) Limit FR-033 to union inputs and open a separate issue for `object`.

## Assumptions

- The SDK 11 RC behaviour measured on `11.0.100-rc.1.26425.128` (Roslyn 5.9.0, STJ 11.0.0-rc.1) holds until GA. Any later change is contained by NFR-002.
- Consumers that write unions already use `LangVersion preview` and SDK 11. Metano does not polyfill `IUnion` for net10 projects.
- The released tool's host framework does not limit parsing (Roslyn does). Project evaluation runs in Roslyn's BuildHost child process, which rolls forward to the newest runtime (research R3).

## Dependencies

- PR #298 (#279): the single runtime-type-test classification (FR-019, NFR-004). It is open and owned by another agent; this spec depends on its capability, not its names, and adds the `array` category to it (FR-019). Primitive-test goldens are finalized after it merges.
- #268 (polymorphic JSON): `[JsonPolymorphic]` union cases (OD-7).
- The non-object `JsonSerializerContext` root fix (batch-3 `json-collection-root`): union roots (OD-8).
- Existing `[GenerateGuard]` machinery: `[PlainObject]` / interface case tests.
