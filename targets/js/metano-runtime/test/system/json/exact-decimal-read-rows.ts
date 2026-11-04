import { Decimal } from "decimal.js";
import { JsonSerializer, type ValueSpec } from "#/system/json";

/**
 * What System.Text.Json reads from JSON text as a `System.Decimal` (#457),
 * measured on .NET 10.0.12 (System.Text.Json 10.0.0.0, source-generated
 * context, `JsonSerializer.Deserialize(string)` then `Serialize`): `stj` is
 * the text it writes back, or `error` the `JsonException` message. `metano`
 * is set only where decimal.js cannot hold what STJ writes: the scale
 * (trailing zeros). `web` rows read under `JsonSerializerDefaults.Web`
 * (`AllowReadingFromString`).
 */
export type DecimalReadRow = {
  readonly json: string;
  readonly stj?: string;
  readonly metano?: string;
  readonly error?: string;
  readonly web?: boolean;
};

const CANNOT_CONVERT = "The JSON value could not be converted to System.Decimal.";

export const DECIMAL_READ_ROWS: readonly DecimalReadRow[] = [
  // Exact digits, beyond what a double holds.
  { json: "12345678901234567890.123456789", stj: "12345678901234567890.123456789" },
  { json: "79228162514264337593543950335", stj: "79228162514264337593543950335" },
  { json: "-79228162514264337593543950335", stj: "-79228162514264337593543950335" },
  { json: "7.9228162514264337593543950335e28", stj: "79228162514264337593543950335" },
  { json: "0.30000000000000004", stj: "0.30000000000000004" },
  { json: "9007199254740993", stj: "9007199254740993" },
  { json: "1e28", stj: "10000000000000000000000000000" },
  { json: "1E+2", stj: "100" },
  { json: "-1.5E-3", stj: "-0.0015" },
  { json: "1.5e3", stj: "1500" },
  { json: "-0", stj: "0" },
  { json: "1.10", stj: "1.10", metano: "1.1" },
  { json: "-0.0", stj: "0.0", metano: "0" },
  // The range: rounding past MaxValue is out of it.
  { json: "79228162514264337593543950335.4", stj: "79228162514264337593543950335" },
  { json: "79228162514264337593543950335.5", error: CANNOT_CONVERT },
  { json: "79228162514264337593543950336", error: CANNOT_CONVERT },
  { json: "99999999999999999999999999999.5", error: CANNOT_CONVERT },
  { json: "1e29", error: CANNOT_CONVERT },
  // A scale of at most 28: smaller values round, or read as zero.
  { json: "1e-28", stj: "0.0000000000000000000000000001" },
  { json: "1e-29", stj: "0.0000000000000000000000000000", metano: "0" },
  { json: "1.5e-29", stj: "0.0000000000000000000000000000", metano: "0" },
  { json: "5e-29", stj: "0.0000000000000000000000000000", metano: "0" },
  { json: "9.9e-29", stj: "0.0000000000000000000000000001" },
  { json: "1e-400", stj: "0.0000000000000000000000000000", metano: "0" },
  { json: "0e-50", stj: "0.0000000000000000000000000000", metano: "0" },
  // Rounding: half to even within 28 digits, half up as the 30th digit.
  { json: "1.5e-28", stj: "0.0000000000000000000000000002" },
  { json: "2.5e-28", stj: "0.0000000000000000000000000002" },
  { json: "3.5e-28", stj: "0.0000000000000000000000000004" },
  { json: "2.50000000000000000000001e-28", stj: "0.0000000000000000000000000003" },
  { json: "0.33333333333333333333333333333333", stj: "0.3333333333333333333333333333" },
  { json: "0.66666666666666666666666666666666", stj: "0.6666666666666666666666666667" },
  { json: "0.12345678901234567890123456789", stj: "0.1234567890123456789012345679" },
  { json: "0.12345678901234567890123456785", stj: "0.1234567890123456789012345678" },
  { json: "0.12345678901234567890123456775", stj: "0.1234567890123456789012345678" },
  { json: "8000000000000000000000000002.5", stj: "8000000000000000000000000002" },
  { json: "8000000000000000000000000001.5", stj: "8000000000000000000000000002" },
  { json: "1.00000000000000000000000000025", stj: "1.0000000000000000000000000003" },
  { json: "1.23456789012345678901234567885", stj: "1.2345678901234567890123456789" },
  { json: "12345678901234567890123456788.5", stj: "12345678901234567890123456789" },
  { json: "79228162514264337593543950334.5", stj: "79228162514264337593543950335" },
  { json: "7922816251426433759354395033.45", stj: "7922816251426433759354395033.5" },
  { json: "7.92281625142643375935439503355", stj: "7.922816251426433759354395034" },
  { json: "9.9999999999999999999999999999", stj: "10.000000000000000000000000000", metano: "10" },
  { json: "0.99999999999999999999999999995", stj: "1.0000000000000000000000000000", metano: "1" },
  // Strings: rejected by default, read with the same grammar and rounding under Web.
  { json: '"1.5"', error: CANNOT_CONVERT },
  { json: '"1.5"', stj: "1.5", web: true },
  { json: '"1.5e3"', stj: "1500", web: true },
  { json: '"12345678901234567890.123456789"', stj: "12345678901234567890.123456789", web: true },
  {
    json: '"0.33333333333333333333333333333333"',
    stj: "0.3333333333333333333333333333",
    web: true,
  },
  { json: '"1e-29"', stj: "0.0000000000000000000000000000", metano: "0", web: true },
  { json: '"79228162514264337593543950335.5"', error: CANNOT_CONVERT, web: true },
  { json: '" 1"', error: CANNOT_CONVERT, web: true },
  { json: '""', error: CANNOT_CONVERT, web: true },
  { json: '"NaN"', error: CANNOT_CONVERT, web: true },
];

/**
 * A sample of the 20019 `System.Decimal` values of the #458 probe (the 19
 * fixed ones and every 200th random one), as STJ writes them. Each text read
 * by `deserializeText` and written back must be STJ's, except for the scale;
 * the whole set was checked the same way on Node 24.11 and Bun 1.4.2.
 */
export const STJ_DECIMAL_TEXTS: readonly string[] = [
  "79228162514264337593543950335",
  "-79228162514264337593543950335",
  "0",
  "0.0",
  "0",
  "0.0000000000000000000000000000",
  "0.0000000000000000000000000001",
  "-0.0000000000000000000000000001",
  "1.10",
  "100",
  "0.0000001",
  "0.0000001",
  "0.3333333333333333333333333333",
  "0.6666666666666666666666666667",
  "10000000000000000000000000000",
  "-10000000000000000000000000000",
  "12345678901234567890123456789",
  "7.9228162514264337593543950335",
  "0.3",
  "-5062.83022042497399429",
  "-24447.9585465",
  "-99213709.4924171098061588605",
  "-0.0000000000000000000000000007",
  "-0.000000000009",
  "29459",
  "7749721930817586.086",
  "0.00000734924",
  "24680845144463885269.1803713",
  "0.000000000000000858579277055",
  "-288791511.636760874551511809",
  "0.00000000000000000632894",
  "8502",
  "229357615713",
  "0.0000065027098418634732",
  "-215.8594322249331",
  "1299006148513001.2075073611269",
  "4473380.6717727316308",
  "-19014418.2165",
  "-0.00000000000000001223314",
  "0.0000414965341",
  "-711.326129104392157760",
  "6678084738028715870.027444",
  "-485766662811232519.94",
  "0.00012918918749645428544",
  "-0.0000000000000011",
  "-0.00001943930075870",
  "31.2304031988099376181",
  "-272254030368.3949116635522771",
  "378120947.2616732213030737",
  "6743333688745.324519947901",
  "-4444697984503.5717849009",
  "-0.13628141557473950455271213",
  "0.0038287696",
  "0.00003687024597516",
  "33.865611",
  "0.01244178059084300871374",
  "0.00000000000000",
  "-332257492511572231181.75",
  "0.0000000000135",
  "-164562491319567.5405467659086",
  "-898117172260.0042049689",
  "0.0000000000258867988354",
  "-0.0000000000000000861849686825",
  "42528979708776916.22",
  "0.0000000030496187899749921",
  "2",
  "13.59936429543656289971",
  "2.6193710861773",
  "-0.00000000231061247941412",
  "0.06130203091",
  "29684186887.2067329243589523",
  "-0.00000000000006935270271",
  "-2946015731.71565",
  "-46.8820180418",
  "-1701230521.11",
  "0.00000000003442427124556",
  "-0.0000000486602616",
  "-0.00002209045",
  "16955.78920083198113487267",
  "-7.7413601167666928201221",
  "57672682829354869892299.510641",
  "-0.000000000000000000000573",
  "-56900.100957148",
  "0.0000000000000000256648634",
  "-18486456161613363564244.02379",
  "-9.58014",
  "-22753850037605900772.96925011",
  "-2072.1342",
  "-2785972.7938669820843776",
  "-985708017163196.874169744535",
  "45169.1055429076257397415869",
  "-217.5968305608891",
  "0.000000000321817",
  "-0.000000001828",
  "-0.000014",
  "0.01737471654",
  "-0.00047089720",
  "540584396821860.5399704",
  "0.0000000000000000000000000075",
  "0.00000",
  "-1149014748167.0441",
  "-78838335876941.4",
  "-0.0000000000010905044",
  "0.000000000185420",
  "-0.111756266126",
  "-6378430890950656562.9",
  "9.7",
  "-0.000184840200207198414",
  "-678212850414896129808330",
  "0.0000067934",
  "7510082.5154967620530794921",
  "0.005108352620780479",
  "-17972220394.458961282175151",
  "138352050078939878488983.3",
  "0.000000000000000000000609",
  "0.980801564844114",
  "-0.91076288322732922419938559",
  "201690",
  "-10475.0896960341405",
];

export const decimalSpec: ValueSpec<Decimal | null> = {
  descriptor: { kind: "decimal", type: Decimal },
};

export const webDecimalSpec: ValueSpec<Decimal | null> = {
  ...decimalSpec,
  readOptions: { numberHandling: "allowReadingFromString" },
};

/** What the runtime must give for the row: STJ's text except for the scale, or its error. */
export function expectedReadOf(row: DecimalReadRow): string {
  return row.error ? `JsonException: ${row.error}` : (row.metano ?? row.stj!);
}

/** The text `deserializeText` reads from the row and `serialize` writes back, or the error. */
export function readOutcomeOf(row: DecimalReadRow): string {
  const spec = row.web ? webDecimalSpec : decimalSpec;

  try {
    return JSON.stringify(
      JsonSerializer.serialize(JsonSerializer.deserializeText(row.json, spec), spec),
    );
  } catch (error) {
    const { name, message } = error as Error;

    return `${name}: ${message}`;
  }
}

/** STJ's text without its scale: the trailing zeros of the fraction, then a bare point. */
export function withoutScale(text: string): string {
  return text.includes(".") ? text.replace(/\.?0+$/, "") : text;
}

/** `text` read by `deserializeText` and written back. */
export function textRoundTripOf(text: string): string {
  return JSON.stringify(
    JsonSerializer.serialize(JsonSerializer.deserializeText(text, decimalSpec), decimalSpec),
  );
}
