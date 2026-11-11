import { checkedInt32, checkedNarrowNumber, checkedUInt32, int32FromDouble, intDiv } from "metano-runtime";

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

export function shiftedByteToUShort(a: number): number {
  return a << 8;
}

export function shiftedByteToUShortReduced(a: number): number {
  return (a << 9) & 0xFFFF;
}

export function shiftedByteToShort(a: number): number {
  return a << 7;
}

export function shiftedByteToShortReduced(a: number): number {
  return ((a << 8) << 16) >> 16;
}

export function nibblePack(a: number, b: number): number {
  return (a & 15) << 4 | b & 15;
}

export function nibblePackReduced(a: number, b: number): number {
  return ((a & 31) << 4 | b & 15) & 0xFF;
}

export function uIntShiftedBelowBit31(source: number): number {
  return (source & 127) << 24;
}

export function uIntShiftedIntoBit31(source: number): number {
  return ((source & 255) << 24) >>> 0;
}

export function byteShiftedBelowBit31(a: number): number {
  return a << 23;
}

export function byteShiftedIntoBit31(a: number): number {
  return (a << 24) >>> 0;
}

export function shiftedBytesSumChecked(a: number, b: number): number {
  return (a << 8) + b;
}

export function shiftedIntSumChecked(source: number, b: number): number {
  return checkedInt32((source << 8) + b);
}

export function shiftBy31(source: number): number {
  return source << 31;
}

export function shiftBy32(source: number): number {
  return source << 32;
}

export function shiftBy33(source: number): number {
  return source << 33;
}

export function shiftByMinusOne(source: number): number {
  return source << -1;
}

export function uIntShiftBy31(source: number): number {
  return (source << 31) >>> 0;
}

export function uIntShiftBy32(source: number): number {
  return (source << 32) >>> 0;
}

export function uIntShiftBy33(source: number): number {
  return (source << 33) >>> 0;
}

export function uIntShiftRightByMinusOne(source: number): number {
  return source >>> -1;
}

export function shiftConstants(): string {
  return `${-2147483648}|${1}|${2}|${-2147483648}|${2147483648}|${1}|${-1073741824}|${1 << 4}`;
}

export function addShiftConstant(source: number): number {
  return intDiv(source, 2) + 1 + (1 << 4);
}

export function andNegativeToByte(source: number): number {
  return (source & -256) & 0xFF;
}

export function orNegativeToSByte(a: number): number {
  return ((a | -64) << 24) >> 24;
}

export function xorNegativeToShort(a: number): number {
  return ((a ^ -1) << 16) >> 16;
}

export function orNegativeToUInt(source: number): number {
  return (source | -2147483648) >>> 0;
}

export function andNegativeOperands(source: number, b: number): number {
  return source & b;
}

export function uLongProductToByte(a: bigint, b: bigint): number {
  return Number(BigInt.asIntN(32, BigInt.asUintN(64, a * b))) & 0xFF;
}

export function receiverShiftedLeft(start: number, count: number): string {
  const cells = new ShiftCells(start);
  cells.next().signed <<= count;

  return `${cells.signed}|${cells.evaluations}`;
}

export function receiverShiftedRight(start: number, count: number): string {
  const cells = new ShiftCells(start);
  cells.next().signed >>= count;

  return `${cells.signed}|${cells.evaluations}`;
}

export function receiverShiftedRightLogical(start: number, count: number): string {
  const cells = new ShiftCells(start);
  ((receiver$temp$0) => receiver$temp$0.signed = (receiver$temp$0.signed >>> count) | 0)(cells.next());

  return `${cells.signed}|${cells.evaluations}`;
}

export function receiverUnsignedShiftedRight(start: number, count: number): string {
  const cells = ((receiver$temp$0) => {
    receiver$temp$0.unsigned = start;

    return receiver$temp$0;
  })(new ShiftCells(0));
  cells.next().unsigned >>>= count;

  return `${cells.unsigned}|${cells.evaluations}`;
}

export function receiverUnsignedShiftedLeft(start: number, count: number): string {
  const cells = ((receiver$temp$0) => {
    receiver$temp$0.unsigned = start;

    return receiver$temp$0;
  })(new ShiftCells(0));
  ((receiver$temp$1) => receiver$temp$1.unsigned = (receiver$temp$1.unsigned << count) >>> 0)(cells.next());

  return `${cells.unsigned}|${cells.evaluations}`;
}

export function receiverByteShiftedLeft(start: number, count: number): string {
  const cells = ((receiver$temp$0) => {
    receiver$temp$0.small = start;

    return receiver$temp$0;
  })(new ShiftCells(0));
  ((receiver$temp$1) => receiver$temp$1.small = (receiver$temp$1.small << count) & 0xFF)(cells.next());

  return `${cells.small}|${cells.evaluations}`;
}

export function receiverElementShifted(start: number, count: number): string {
  const cells = new ShiftCells(start);
  let index = 0;
  cells.next().slots[index++] <<= count;

  return `${cells.slots[0]}|${cells.evaluations}|${index}`;
}

export function indexAndCountEvaluatedOnce(start: number, count: number): string {
  const cells = new ShiftCells(start);
  cells.slots[cells.nextIndex()] <<= cells.nextCount(count);

  return `${cells.slots[1]}|${cells.indexEvaluations}|${cells.countEvaluations}`;
}

export function receiverPropertyShifted(start: number, count: number): string {
  const cells = new ShiftCells(start);
  cells.next().tracked <<= count;

  return `${cells.signed}|${cells.evaluations}|${cells.reads}|${cells.writes}`;
}

export function arrayElementReceiverShifted(start: number): string {
  const all = [new ShiftCells(start), new ShiftCells(start)];
  all[0].slots[1] <<= 2;

  return `${all[0].slots[1]}|${all[1].slots[1]}`;
}

export async function receiverShiftedByAwaitedCount(start: number, count: number): Promise<string> {
  const cells = new ShiftCells(start);
  cells.next().signed <<= await Promise.resolve(count);

  return `${cells.signed}|${cells.evaluations}`;
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

export class ShiftCells {
  signed: number = 0;

  unsigned: number = 0;

  small: number = 0;

  slots: number[];

  evaluations: number = 0;

  indexEvaluations: number = 0;

  countEvaluations: number = 0;

  reads: number = 0;

  writes: number = 0;

  constructor(seed: number) {
    this.signed = seed;
    this.slots = [seed, seed];
  }

  get tracked(): number {
    this.reads++;

    return this.signed;
  }

  set tracked(value: number) {
    this.writes++;
    this.signed = value;
  }

  next(): ShiftCells {
    this.evaluations++;

    return this;
  }

  nextIndex(): number {
    this.indexEvaluations++;

    return 1;
  }

  nextCount(count: number): number {
    this.countEvaluations++;

    return count;
  }
}
