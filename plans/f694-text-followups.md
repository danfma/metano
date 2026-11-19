# Review follow-ups of #694

Scope: fix FR-051 default-text lowering and its descriptor reachability; no new feature or requirement. Work only in oc/f694-text-followups. No review agents (wave brief), push, or PR.

## Phase 1: Shared default-text decision
Status: Complete
- [x] Share target-aware eligibility and text-site rules between extraction and runtime scanning.
- [x] Add ignored-enum regression and coverage of every shared site.
- [x] Replace the decimal package-string test with the declarative formatting lookup.

### Verification Plan
- Build and run Metano.Tests with existing goldens unchanged.
- Confirm generated samples remain byte-identical with verify-targets-clean.

### Phase Summary
`DefaultTextSites` owns interpolation, built-in string addition/compound addition, parameterless BCL ToString and supported Join classification. `DefaultTextConversionPolicy` supplies the same target, mapping predicate and site to extraction and reachability. Ignore is checked explicitly because IsTranspilableType gives Transpile precedence. The decimal bridge consults the first applicable parameterless ToString mapping, not an import specifier. Final focused tests: 28/28. No FR amendment needed (FR-051 already excludes enums without descriptors).

## Phase 2: Decimal allocation evaluation
Status: Complete
- [x] Evaluate a simple pure module-level literal hoist; otherwise measure b2.mjs and document why not.
- [x] Verify runtime and golden side-effect imports bundle to 0 B.

### Verification Plan
- Run Node and Bun benchmark under timeouts and retain observations.
- Run tree-shaking gates.

### Phase Summary
No blanket hoist: decimal.js exposes a mutable coefficient array, and exported generated methods may return literals to JS. A Node/Bun experiment mutating the first result's d[0] shows fresh evaluation still returns 1.20, while a pooled literal returns 9.20 and the same object. An escape-restricted optimization and group-scoped literal pool would be a separate design, not a generally safe literal substitution. b2.mjs was adapted only to replace its now-missing rt-head/rt-base aliases with the current built runtime and the identical pre-#694 clone configuration. Three independent runs per engine: original ctor+plus medians base/head/scale are Node 105.90/105.85/159.10 ms, Bun 82.85/83.80/103.60 ms. The additional matched-literal loop measures Node fresh/scale/unsafe-hoist 108.4/163.5/36.9 ms, Bun 86.0/106.4/18.7 ms (15 observations each). Runtime side-effect-import suite: 352 passed; golden bundle tests passed in the full .NET suite. Existing empty allowlists remain unchanged.

## Phase 3: Full validation and local commit
Status: Validation complete; ready for local commit
- [x] Run the full wave gate script and inspect all results, including 0 CHANGED conformance.
- [x] Run rule checks, review the diff, prepare local commit with Part of #694.
- [x] Prepare /Volumes/Work/tmp/oc-logs/oc-f694.REPORT.md with evidence; record final HEAD after commit.

### Verification Plan
- All gates green and git status empty after commit.

### Phase Summary
Final full gates: build 0 errors, 1 existing sample warning; .NET 4674 passed, 0 failed, 7 skipped; runtime 3002 passed; sample suites 19/186/33/25 passed. Conformance: 723 rows each, Bun 620 MATCH/103 KNOWN GAP and Node 614 MATCH/109 KNOWN GAP, 0 CHANGED. Eight DefaultValueTextProbes rows also MATCH on both engines against live .NET. verify-targets-clean=0; no changes to existing goldens, samples or recordings. Rule checks and git diff --check pass. The gate script's exit code alone is insufficient: all printed results were inspected.

## Final Recap
The target/reachability and decimal mapping fixes are validated with unchanged generated baselines. Literal hoisting is deferred on demonstrated aliasing grounds, with measured cost retained in the external report. The report records the final local commit SHA and delivery status; no push or PR.

## Deployment Plan
Local commit only; integration is the maintainer's responsibility.
