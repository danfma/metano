# compiler-man Agent Memory

- [IR captured ctor params](ir_captured_ctor_params.md) — `CapturedFieldName` + `IsCapturedByCtor` already exist; extend `AnnotateCapturedParams`, don't redesign IR.
- [Explicit interface lowering](explicit_interface_lowering.md) — #278 fates; generated code + BCL bridges must not raise MS0031; regen samples.
- [Import collection two paths](import_collection_two_paths.md) — cross-package imports need origin-carrying nodes (TsNamedType/TsTypeReference); bare TsIdentifier is never imported.
- [Raw loop import blindspot](raw_loop_import_blindspot.md) — TS loops/try are TsRawStatement; walkers must descend into its Embedded* lists (fixed in #307).
- [Dart operand grouping gap](dart_operand_grouping_gap.md) — Dart printer misgroups conditional/throw operands; always dart-analyze probes for core lowerings.
- [Synthesized calls bypass rewriters](synthesized_calls_bypass_rewriters.md) — symbol-built IR skips [Emit], params spread, extension $set; probe these.
- [NoContainer module state](nocontainer_module_state.md) — module fields lower via TsModuleExportReference; cross-file writes are MS0001.
- [Target-specific lowering placement](target_specific_lowering_placement.md) — Dart ignores params/spread/setters; TS-shaped fixes go in the TS bridge, facts in core.
