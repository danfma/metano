import { compareInt64, formatInt64, objectEquals, parseInt64, parseUInt64, tryParseInt64, tryParseUInt64 } from "metano-runtime";

export class Text {
  constructor() { }

  format(l: bigint, u: bigint, format: string): string {
    return formatInt64(l, "N0") + formatInt64(u, "X") + formatInt64(l, format);
  }

  plain(l: bigint): string {
    return l.toString() + `${l}` + l.toString();
  }

  holes(l: bigint, u: bigint): string {
    return `${formatInt64(l, "X")}|${formatInt64(l, null, 8)}|${formatInt64(u, "D2", -4)}`;
  }

  nullableHole(l: bigint | null): string {
    return `${l ?? ""}`;
  }

  intHole(i: number): string {
    return `${i}`;
  }

  parse(s: string): bigint {
    return BigInt.asIntN(64, BigInt.asIntN(64, parseInt64(s) + parseInt64(s, 515)) + parseInt64(s, 71));
  }

  uParse(s: string): bigint {
    return parseUInt64(s, 111);
  }

  tryParse(s: string): bigint {
    const [$found0, x] = tryParseInt64(s);

    return $found0 ? x : -1n;
  }

  uTryParse(s: string): bigint {
    const [$found0, x] = tryParseUInt64(s, 515);

    return $found0 ? x : 0n;
  }

  compare(l: bigint, m: bigint, u: bigint, i: number): number {
    return compareInt64(l, m) + compareInt64(u, u) + compareInt64(l, BigInt(i));
  }

  equal(l: bigint, m: bigint, i: number, o: Object): boolean {
    return (l === m) && (l === BigInt(i)) && objectEquals(l, o);
  }
}
