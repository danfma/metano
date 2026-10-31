---
name: explicit-interface-lowering
description: How explicit interface implementations lower (#278) and the BCL-bridge trap that sample regen exposed
metadata:
  type: project
---

Explicit interface implementations are decided in `Frontend/Roslyn/ExplicitImplementationLowering` (shared by `IrClassExtractor` and `ExplicitImplementationValidator`, MS0031).

**Why:** A plain "conflict => error" rule broke SampleTodo/SampleIssueTracker on the first regen: the System.Text.Json source generator adds `IJsonTypeInfoResolver.GetTypeInfo` next to `GetTypeInfo(Type)`. The idiomatic BCL bridge `IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();` hits the same rule. Two fixes followed: generated members are exempt from the validator, and interfaces outside the transpiled contract get the `Superseded` fate (dropped silently).

**How to apply:** Any new validator over transpilable members must exempt `GeneratedSourceTrees` output. Always regenerate the samples (delete `.metano-stamp`) before trusting the unit tests. Related: [[ir-captured-ctor-params]].
