---
name: synthesized-calls-bypass-rewriters
description: IR built directly from symbols (not from InvocationExpressionSyntax / AssignmentExpressionSyntax) skips the extractor's invocation-rewriter chain and extension-property lowering
metadata:
  type: project
---

When an extractor lowering synthesizes `IrCallExpression` / member assignments itself (e.g. the #254 object/collection initializer lowering in `IrExpressionExtractor.ObjectInitializers.cs`), it bypasses everything `ExtractInvocationCore` and `ExtractAssignment` do: `[Emit]`/`[Import]` templates (EmitOrImportRewriter), `ApplyParamsSpread`, C# 14 extension-property `$set` lowering, extension indexers, ObjectArgs wrapping. Verified by CLI probe on 2026-10-31: `[Emit]` Add -> plain `.add(1)`, `{ arr }` into `params int[]` Add -> `add(arr)` unspread, extension property in initializer -> plain field write.

**Why:** these paths are keyed on syntax nodes, so symbol-built IR never reaches them.

**How to apply:** in reviews of any new synthesized-call lowering, probe [Emit], params, extension members, overloads, [Name]; prefer building from `IOperation` (`GetOperation(element)` gives the bound Add invocation) or reusing the shared builders. Also: an `init` accessor with a body emits getter-only in TS, so any runtime write (Object.assign) to it throws. See [[ir-captured-ctor-params]].
