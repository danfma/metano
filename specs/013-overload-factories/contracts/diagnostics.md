# Contract: Diagnostic `MS0067`

**Normative for**: 013 FR-004, FR-005, FR-020. This spec PR reserves `MS0067` in
[diagnostic-catalog.md](../../001-project-baseline-evolution/baseline/diagnostic-catalog.md);
`MS0066` is the last allocated code (spec 012). No other code is needed: the direct-call rewrite
has no failure mode that is not already a compile error in the consumer, and the climb's dead end
reuses `MS0003`.

| Code | Name | Severity | Location |
|---|---|---|---|
| `MS0067` | `OverloadNameShadowed` | Warning | the overload whose computed name was `$`-resolved |

## Reasons

1. A constructor factory, a selector suffix or a fast-path name computed by the naming policy is
   claimed by a user-written member of the same scope (a class member, or a namespace function in
   the companion / `[ExportedAsModule]` scope); the emitted name gets `$` appended until free.

Example: `Factory 'Money.fromCents' collides with the user member 'fromCents'; the generated factory is emitted as 'fromCents$' (spec 013 FR-004).`

The warning is what makes the `$` fallback acceptable on public API: the rename is silent in C#
terms (nothing in the C# source changed), so it must not be silent in the build. One report per
overload, at the overload's location, naming the claimed name, the claiming member and the emitted
name.

Not reported: a `$` suffix arising from two synthesized names colliding with each other (the
policy's passes 1–3 already guarantee distinctness within the group; a cross-group `$` within the
same scope is still `MS0067`).
