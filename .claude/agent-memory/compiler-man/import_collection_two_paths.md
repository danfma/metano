---
name: import-collection-two-paths
description: How the TS ImportCollector resolves cross-package vs intra-project imports, and why value-position refs need an origin-carrying AST node
metadata:
  type: project
---

The TS `ImportCollector` walks the emitted TS AST and resolves imports through two channels:
cross-package names come ONLY from origin-carrying nodes (`TsNamedType.Origin` in type positions,
`TsTypeReference(Name, Origin)` in value positions); every other identifier is resolved by NAME
against own-project maps (`TranspilableTypes`, guards, `NoContainerFunctionExports`, BCL/[Import]).
A cross-package type that reaches the TS AST as a bare `TsIdentifier` is silently never imported (TS2304).

**Why:** #286 — `TsTypeReference` had been dead since the IR migration; `IrTypeReference` carried no
origin, so `new X()`, `Enum.Member`, `x instanceof X` across packages lost their import. Fixed by
`IrTypeReference.Origin` + bridge `TypeValueReference(name, origin)`.

**How to apply:** any new bridge path that emits a type NAME in value position must go through
`TypeValueReference` (or emit `TsTypeReference`) when the IR has an origin; reviews should flag
`new TsIdentifier(named.Name)` on a possibly cross-package type. Value-ness is merged by name
(`valueNames`), so a name used both in a signature and a value position gets a value import.
Known remaining gaps (2026-10-31): `as` expressions and property patterns are unsupported in the extractor.
