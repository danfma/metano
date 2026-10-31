# compiler-man Agent Memory

- [IR captured ctor params](ir_captured_ctor_params.md) — `CapturedFieldName` + `IsCapturedByCtor` already exist; extend `AnnotateCapturedParams`, don't redesign IR.
- [Explicit interface lowering](explicit_interface_lowering.md) — #278 fates; generated code + BCL bridges must not raise MS0031; regen samples.
- [Import collection two paths](import_collection_two_paths.md) — cross-package imports need origin-carrying nodes (TsNamedType/TsTypeReference); bare TsIdentifier is never imported.
