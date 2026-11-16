import { alignValue, formatBoolean, formatDouble, formatInt64, formatInteger } from "metano-runtime";

export class Clauses {
  constructor() { }

  integers(i: number, u: number, s: number, us: number, sb: number, b: number): string {
    return `${formatInteger(i, 32, "D2")}|${formatInteger(u, 32, "X8")}|${formatInteger(s, 16, "X")}|${formatInteger(us, 16, "N0", 6)}|${formatInteger(sb, 8, "x")}|${formatInteger(b, 8, "D3", -4)}`;
  }

  doubles(d: number): string {
    return `${formatDouble(d, "F2")}|${formatDouble(d, "N1", 10)}|${formatDouble(d, "E3")}|${formatDouble(d, "P0")}|${formatDouble(d, "G5")}|${formatDouble(d, "C")}|${formatDouble(d, "R")}|${formatDouble(d, "e")}`;
  }

  sixtyFourBits(l: bigint, u: bigint): string {
    return `${formatInt64(l, "E2")}|${formatInt64(u, "P1", 8)}|${formatInt64(l, "C0")}`;
  }

  nullables(i: number | null, d: number | null, l: bigint | null): string {
    return `${formatInteger(i, 32, "D2")}|${formatDouble(d, "F1", 8)}|${formatInt64(l, "N0")}`;
  }

  alignments(i: number, d: number, s: string, b: boolean, n: string | null): string {
    return `[${alignValue(i, 5)}][${alignValue(formatDouble(d), -8)}][${alignValue(s, 4)}][${alignValue(formatBoolean(b), 6)}][${alignValue(n, 3)}]`;
  }

  ignoredFormats(s: string, b: boolean, c: string): string {
    return `${s}|${formatBoolean(b)}|${alignValue(c, 3)}`;
  }

  toStrings(i: number, d: number, b: number, s: number): string {
    return formatInteger(i, 32, "D4") + formatDouble(d, "F3") + formatInteger(b, 8, "X2") + formatInteger(s, 16, "x");
  }
}
