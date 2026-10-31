---
name: raw-loop-import-blindspot
description: TS loops/try are TsRawStatement strings; walkers only see their contents through EmbeddedStatements/EmbeddedExpressions
metadata:
  type: project
---

`IrToTsStatementBridge` renders `for`/`foreach`/`while`/`do`/`try` to a string (`WrapLoop` → `TsRawStatement`).
`ImportCollector` walks the TS AST only and `IrRuntimeRequirementScanner` does not walk method bodies, so until
#307 anything used only inside a loop lost its import (`intDiv` from `TsTemplate.RuntimeImports`, `new Item()`).
#307 fixed it by giving `TsRawStatement` `EmbeddedStatements` / `EmbeddedExpressions` (the mapped nodes the text was
printed from), walked by `ImportCollector` and the JSX detector in `TypeTransformer`.

**Why:** a new runtime-helper lowering for a common operator turned the latent gap into a regression.
**How to apply:** any new raw-text statement must fill the Embedded lists; any new AST walker must descend into
them. When reviewing a new import-requiring node, still probe it inside a loop in a file with no other occurrence.
See [[import-collection-two-paths]].
