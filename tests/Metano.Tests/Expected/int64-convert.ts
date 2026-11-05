import { convertDecimalToInt64, convertDoubleToInt64, convertDoubleToUInt64, convertNarrow, convertObjectToInt64, convertStringToInt64, convertStringToUInt64, int64ToSingle } from "metano-runtime";
import { Decimal } from "decimal.js";

export class Converts {
  constructor() { }

  toInt64(d: number, f: number, m: Decimal, s: string, b: boolean, c: string, i: number): bigint {
    return BigInt.asIntN(64, BigInt.asIntN(64, BigInt.asIntN(64, BigInt.asIntN(64, BigInt.asIntN(64, BigInt.asIntN(64, BigInt.asIntN(64, convertDoubleToInt64(d) + convertDoubleToInt64(f)) + convertDecimalToInt64(m)) + convertStringToInt64(s)) + convertStringToInt64(s, 16)) + (b ? 1n : 0n)) + BigInt(c.charCodeAt(0))) + BigInt(i));
  }

  fromWide(u: bigint, o: Object): bigint {
    return BigInt.asIntN(64, convertNarrow(u, "Int64") + convertObjectToInt64(o));
  }

  toUInt64(d: number, i: number, l: bigint, ui: number, s: string): bigint {
    return BigInt.asUintN(64, BigInt.asUintN(64, BigInt.asUintN(64, BigInt.asUintN(64, convertDoubleToUInt64(d) + convertNarrow(BigInt(i), "UInt64")) + convertNarrow(l, "UInt64")) + BigInt(ui)) + convertStringToUInt64(s, 2));
  }

  narrow(l: bigint, u: bigint): number {
    return Number(convertNarrow(l, "Int32")) + Number(convertNarrow(u, "Int16")) + Number(convertNarrow(l, "Byte")) + Number(convertNarrow(l, "SByte")) + Number(convertNarrow(l, "UInt16")) + Number(convertNarrow(u, "UInt32"));
  }

  character(l: bigint): string {
    return String.fromCharCode(Number(convertNarrow(l, "Char")));
  }

  floating(l: bigint, u: bigint): number {
    return Number(l) + int64ToSingle(u);
  }

  truth(l: bigint): boolean {
    return (l !== 0n);
  }
}
