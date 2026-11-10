/**
 * The Node run of the spec 012 reference fixtures (FR-064, SC-001): the same
 * cases as the bun test, over the built `dist`, exiting non-zero on any
 * mismatch. Run with `bun run build && bun run test:node`.
 */

import { FIXTURE_RUNS, loadFixture, mismatchesOf } from "./conformance-runs.ts";

const reports = await Promise.all(
  FIXTURE_RUNS.map(async ({ name, twin }) => {
    const fixture = loadFixture(name);

    return { name, total: fixture.cases.length, mismatches: await mismatchesOf(fixture, twin) };
  }),
);

const failures = reports.filter(({ total, mismatches }) => total === 0 || mismatches.length > 0);

for (const { name, total, mismatches } of reports) {
  console.log(`${name}: ${total - mismatches.length} of ${total} cases match .NET`);
}

for (const { name, mismatches } of failures) {
  console.error(`${name}:`, JSON.stringify(mismatches.slice(0, 20), null, 2));
}

if (failures.length > 0) {
  process.exit(1);
}
