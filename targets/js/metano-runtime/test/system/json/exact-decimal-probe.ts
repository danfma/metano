/**
 * Prints, as one JSON line, the text the runtime writes for each row of
 * `DECIMAL_WRITE_ROWS` and the outcome of each `DECIMAL_DOMAIN_ROWS` row,
 * so the same check runs under Node (bundled with `bun build`) and Bun. `--without-raw-json` removes `JSON.rawJSON` and
 * `JSON.isRawJSON` before the runtime loads, as on an engine without them.
 */
const withoutRawJson = process.argv.includes("--without-raw-json");

if (withoutRawJson) {
  const json = JSON as { rawJSON?: unknown; isRawJSON?: unknown };

  delete json.rawJSON;
  delete json.isRawJSON;
}

const { DECIMAL_DOMAIN_ROWS, DECIMAL_WRITE_ROWS, domainOutcomeOf, writtenTextOf } = await import(
  "./exact-decimal-rows.ts"
);

console.log(
  JSON.stringify({
    written: DECIMAL_WRITE_ROWS.map(writtenTextOf),
    domain: DECIMAL_DOMAIN_ROWS.map(domainOutcomeOf),
  }),
);
