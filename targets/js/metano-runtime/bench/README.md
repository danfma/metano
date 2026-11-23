# metano-runtime benchmarks

`collections.bench.ts` is the NFR-001 / FR-039 gate of spec 011 (see
`specs/011-runtime-collections-and-types/plan.md` § Benchmark plan). Its numbers are
only meaningful on a quiet machine; the gating measurement was recorded by issue #720 (stage 011g).

## Quiet-machine measurement (#720)

1. Quit side tasks (editors indexing, browsers, builds, agents) and check the load average:
   `uptime` must report a 1-minute load below 2.
2. Build both dists: `bun run build`.
3. Run both engines, which are both the gate (record the engine versions the harness prints):

   ```sh
   bun run bench        # Bun
   bun run bench:node   # Node.js (the LTS line ci.yml pins)
   ```

   `--case <text>` filters by name, `--check` fails on a missed budget and
   `--json <file>` saves the rows.
4. Record the raw and normalized tables, `C_measured`, the engine versions and the machine
   in `docs/runtime-types.md`. Set final budgets at measured + 10%.
5. Read the reported row "3.0 class vs the 2.x runtime HashSet" (`legacy-hash-set.ts` is the
   vendored 2.x class, bench-only). It is informative, not a gate: the figure is recorded in
   `docs/runtime-types.md`, and a `Set`-backed store inside `HashCore` is a post-3.0 follow-up.
