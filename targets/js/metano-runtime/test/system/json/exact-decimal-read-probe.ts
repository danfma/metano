/**
 * Prints, as one JSON line, what the runtime reads from JSON text (#457):
 * each `DECIMAL_READ_ROWS` outcome, each `STJ_DECIMAL_TEXTS` round trip, and
 * each `JSON_TEXT_ROWS` outcome of the tokenizer and of `JSON.parse` with
 * STJ's errors, so the same check runs under Node (bundled with
 * `bun build`) and Bun.
 */
import { parseJsonText, readJsonText } from "#/system/json/json-text-reader";
import {
  DECIMAL_READ_ROWS,
  readOutcomeOf,
  STJ_DECIMAL_TEXTS,
  textRoundTripOf,
} from "./exact-decimal-read-rows.ts";
import { JSON_TEXT_ROWS, textOutcomeOf } from "./json-text-rows.ts";

console.log(
  JSON.stringify({
    reads: DECIMAL_READ_ROWS.map(readOutcomeOf),
    texts: STJ_DECIMAL_TEXTS.map(textRoundTripOf),
    tokenized: JSON_TEXT_ROWS.map((row) => textOutcomeOf(() => readJsonText(row.text))),
    parsed: JSON_TEXT_ROWS.map((row) => textOutcomeOf(() => parseJsonText(row.text))),
  }),
);
