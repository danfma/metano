---
name: target-specific-lowering-placement
description: Where a lowering that depends on TS rest params / setters / iteration protocol must live, since the Dart backend ignores params, setters and spread flags
metadata:
  type: project
---

The Dart backend ignores `IrArgument.IsSpread`, `IrParameter.IsParams` and property `SetterBody` (verified 2026-10-31, #320). So a fix whose shape comes from a TS construct (rest parameter cannot hold `null`, setter cannot write a `readonly` field, `[Symbol.iterator]`) belongs in the TS bridge, with only target-neutral *facts* in core IR (e.g. `IrTypeSemantics.EnumeratedElementType`, `IrNamedTypeSemantics.IsDefaultConstructible`). Dropping an argument in the core extractor would turn Dart's valid `count(null)` into `count()`.

**Why:** #320 first dropped the null params array in `ApplyParamsSpread` (core); it would have broken Dart and shifted `[Emit]` template `$N` indices.

**How to apply:** in reviews, ask "does Dart (or a `[MapMethod]` template) see this IR too?" before accepting an argument-list rewrite in the extractor. Bridge-reported diagnostics (`ExtractionDiagnostics.Report` from TS bridge) carry no location and are deduped by message. See [[synthesized-calls-bypass-rewriters]].
