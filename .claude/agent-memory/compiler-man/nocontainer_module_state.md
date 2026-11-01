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

**How to apply:** ES imports are read-only, so ImportCollector rejects cross-file writes (MS0001);
computed static props in a containerless class also report MS0001. Dart still drops module
variables, and extension-holder static classes keep no module state. See [[import-collection-two-paths]].
