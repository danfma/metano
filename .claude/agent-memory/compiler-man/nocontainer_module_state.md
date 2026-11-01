---
name: nocontainer-module-state
description: How [NoContainer] static fields/props lower to TS module variables and why their imports go through TsModuleExportReference, not NoContainerFunctionExports
metadata:
  type: project
---

[NoContainer]/[ExportedAsModule] static fields and auto-props are module variables (#314):
`IrModuleVariableExtractor` (core) → `IrToTsModuleBridge.ConvertVariables`. The call-site flatten
emits `TsModuleExportReference(name, owner)` (renamed from TsCompanionFunctionReference) for data
members, so ImportCollector imports by owner.

**Why:** registering field names in `NoContainerFunctionExports` would make the bare-`TsIdentifier`
branch import any local named `max`/`count` (TS2440). Functions still use that name-keyed map.

**How to apply:** semantic mismatches (cross-file writes, static ctor, TDZ forward refs, local
shadowing the bare export name, var/function name clash) are rejected up front by the Roslyn-side
`ModuleVariableValidator` (TS target), with locations — not in ImportCollector. `[Inline]` and
`[External]` members are NOT module state (HtmlElementType.Div in the DOM bindings is `[Inline]`;
counting it as state made the counter samples fail the shadow check). Dart still drops module
variables. See [[import-collection-two-paths]].
