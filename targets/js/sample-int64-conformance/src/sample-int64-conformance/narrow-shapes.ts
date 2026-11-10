import { checkedInt32, checkedNarrowNumber, checkedUInt32, int32FromDouble } from "metano-runtime";

const LowByte: number = 255;
const runtimeLowByte: number = 255;

export function maskToSByte(source: number): number {
  return source & 127;
}

export function wideMaskToSByte(source: number): number {
  return ((source & 255) << 24) >> 24;
}

export function sBytePlus128(a: number): number {
  return a + 128;
}

export function sBytePlus129(a: number): number {
  return (a + 129) & 0xFF;
}

export function bytesProductToUShort(a: number, b: number): number {
  return a * b;
}

export function bytesProductToByte(a: number, b: number): number {
  return (a * b) & 0xFF;
}

export function uIntLowBitsToInt(source: number): number {
  return source & 2147483647;
}

export function uIntAllBitsToInt(source: number): number {
  return ((source & 4294967295) >>> 0) | 0;
}

export function constMaskToByte(source: number): number {
  return source & LowByte;
}

export function readonlyMaskToByte(source: number): number {
  return (source & runtimeLowByte) & 0xFF;
}

export function maskToByteChecked(source: number): number {
  return source & LowByte;
}

export function readonlyMaskToByteChecked(source: number): number {
  return checkedNarrowNumber(source & runtimeLowByte << 1, 8, false);
}

export function intToByteToSByte(source: number): number {
  return ((source & 0xFF) << 24) >> 24;
}

export function uIntToShortToUInt(source: number): number {
  return ((source << 16) >> 16) >>> 0;
}

export function intToUShortToInt(source: number): number {
  return source & 0xFFFF;
}

export function doubleToIntToByte(source: number): number {
  return int32FromDouble(source) & 0xFF;
}

export function longToIntToByte(source: bigint): number {
  return Number(BigInt.asIntN(32, source)) & 0xFF;
}

export function intToByteToSByteChecked(source: number): number {
  return checkedNarrowNumber(checkedNarrowNumber(source, 8, false), 8, true);
}

export function intToShortToUIntMixed(source: number): number {
  return checkedUInt32((source << 16) >> 16);
}

export function staticFieldCompound(start: number, amount: number): number {
  NarrowStatics.count = start;
  NarrowStatics.count = (NarrowStatics.count + amount) & 0xFF;
  NarrowStatics.count = (NarrowStatics.count + 1) & 0xFF;

  return NarrowStatics.count;
}

export function staticFieldCompoundChecked(start: number, amount: number): number {
  NarrowStatics.level = start;

  {
    NarrowStatics.level = checkedNarrowNumber(NarrowStatics.level - amount, 16, true);
    NarrowStatics.level = checkedNarrowNumber(NarrowStatics.level - 1, 16, true);
  }

  return NarrowStatics.level;
}

export function capturedIncrement(start: number): number {
  let counter = start;
  const step = () => ((counter = (counter + 1) & 0xFF) - 1) & 0xFF;
  step();
  step();

  return counter;
}

export function capturedCompoundChecked(start: number, amount: number): number {
  let total = start;

  {
    const accumulate = () => total = checkedNarrowNumber(total + amount, 8, true);
    accumulate();
  }

  return total;
}

export function receiverEvaluatedOnce(start: number, amount: number): number {
  const source = new NarrowSource(start);
  ((receiver$temp$0) => receiver$temp$0.value = (receiver$temp$0.value + amount) & 0xFF)(source.next());

  return source.evaluations * 1000 + source.value;
}

export function receiverIncrementedOnce(start: number): number {
  const source = ((receiver$temp$0) => {
    receiver$temp$0.signed = start;

    return receiver$temp$0;
  })(new NarrowSource(0));
  const previous = ((receiver$temp$1) => (((receiver$temp$1.signed = ((receiver$temp$1.signed + 1) << 24) >> 24) - 1) << 24) >> 24)(source.next());

  return source.evaluations * 1000 + (previous ^ source.signed);
}

export function foreachBytes(values: number[]): number {
  let total = 0;
  for (const value$element of values) {
    const value = value$element & 0xFF;
    total += value;
  }

  return total;
}

export function foreachBytesChecked(values: number[]): number {
  let total = 0;

  {
    for (const value$element of values) {
      const value = checkedNarrowNumber(value$element, 8, false);
      total = checkedInt32(total + value);
    }
  }

  return total;
}

export function tupleLiteralCast(a: number, b: number): number {
  const narrow = [a & 0xFF, (b << 16) >> 16] as [number, number];

  return narrow[0] * 100000 + narrow[1];
}

export function tupleCast(a: number, b: number): number {
  const wide = [a, b] as [number, number];
  const narrow = [(wide[0] << 24) >> 24, wide[1] & 0xFFFF] as [number, number];

  return narrow[0] * 100000 + narrow[1];
}

export function tupleCastChecked(a: number, b: number): number {
  const wide = [a, b] as [number, number];

  {
    const narrow = [checkedNarrowNumber(wide[0], 8, true), checkedNarrowNumber(wide[1], 16, false)] as [number, number];

    return narrow[0] * 100000 + narrow[1];
  }
}

export function uncheckedInsideCheckedCast(source: number): number {
  return checkedNarrowNumber((source & 0xFF) * 200, 16, true);
}

export function checkedInsideUncheckedCast(source: number): number {
  return ((checkedNarrowNumber(source, 16, false) >> 4) << 24) >> 24;
}

export function nestedBlocks(source: number): number {
  {
    const low = source & 0xFF;

    {
      const wrapped = (low << 24) >> 24;

      {
        return wrapped + 128 + checkedNarrowNumber(source, 16, false);
      }
    }
  }
}

export function concatenatedChecked(source: number): string {
  return "v=" + checkedNarrowNumber(source, 8, false) + ";";
}

export function interpolatedChecked(a: number, b: number): string {
  return `${checkedInt32(a + b)}|${a & 0xFF}|${b & 127}`;
}

export class NarrowStatics {
  static count: number = 0;

  static level: number = 0;

  constructor() { }
}

export class NarrowSource {
  value: number = 0;

  signed: number = 0;

  evaluations: number = 0;

  constructor(value: number) {
    this.value = value;
  }

  next(): NarrowSource {
    this.evaluations++;

    return this;
  }
}
