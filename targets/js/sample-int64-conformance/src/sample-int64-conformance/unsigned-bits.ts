export function uIntAnd(a: number, b: number): number {
  return (a & b) >>> 0;
}

export function intAnd(a: number, b: number): number {
  return a & b;
}

export function uIntCompoundAnd(a: number, b: number): number {
  a = (a & b) >>> 0;

  return a;
}

export function uIntOr(a: number, b: number): number {
  return (a | b) >>> 0;
}

export function intOr(a: number, b: number): number {
  return a | b;
}

export function uIntCompoundOr(a: number, b: number): number {
  a = (a | b) >>> 0;

  return a;
}

export function uIntXor(a: number, b: number): number {
  return (a ^ b) >>> 0;
}

export function intXor(a: number, b: number): number {
  return a ^ b;
}

export function uIntCompoundXor(a: number, b: number): number {
  a = (a ^ b) >>> 0;

  return a;
}

export function uIntComplement(a: number): number {
  return ~a >>> 0;
}

export function intComplement(a: number): number {
  return ~a;
}

export function uShortComplement(a: number): number {
  return ~a;
}

export function uIntShiftLeft(a: number, count: number): number {
  return (a << count) >>> 0;
}

export function intShiftLeft(a: number, count: number): number {
  return a << count;
}

export function uIntCompoundShiftLeft(a: number, count: number): number {
  a = (a << count) >>> 0;

  return a;
}

export function intCompoundShiftLeft(a: number, count: number): number {
  a <<= count;

  return a;
}

export function sBytePromotedShiftLeft(a: number, count: number): number {
  return a << count;
}

export function bytePromotedShiftLeft(a: number, count: number): number {
  return a << count;
}

export function uIntShiftRight(a: number, count: number): number {
  return a >>> count;
}

export function intShiftRight(a: number, count: number): number {
  return a >> count;
}

export function uIntCompoundShiftRight(a: number, count: number): number {
  a = a >>> count;

  return a;
}

export function intCompoundShiftRight(a: number, count: number): number {
  a >>= count;

  return a;
}

export function sBytePromotedShiftRight(a: number, count: number): number {
  return a >> count;
}

export function bytePromotedShiftRight(a: number, count: number): number {
  return a >> count;
}

export function uIntShiftRightLogical(a: number, count: number): number {
  return a >>> count;
}

export function intShiftRightLogical(a: number, count: number): number {
  return (a >>> count) | 0;
}

export function uIntCompoundShiftRightLogical(a: number, count: number): number {
  a >>>= count;

  return a;
}

export function intCompoundShiftRightLogical(a: number, count: number): number {
  a = (a >>> count) | 0;

  return a;
}

export function sBytePromotedShiftRightLogical(a: number, count: number): number {
  return (a >>> count) | 0;
}

export function bytePromotedShiftRightLogical(a: number, count: number): number {
  return a >>> count;
}

export function uIntShiftLeftChecked(a: number, count: number): number {
  return (a << count) >>> 0;
}

export function uIntMaskLowByte(a: number): number {
  return a & 255;
}

export function uIntSetHighBit(a: number): number {
  return (a | 2147483648) >>> 0;
}

export function uIntWithByte(a: number, b: number): number {
  return a & b;
}

export function uIntMix(a: number, b: number): number {
  return ((((a ^ a >>> 16) >>> 0 << 5) >>> 0 | b >>> 27) >>> 0 ^ ~b >>> 0) >>> 0;
}

export function uIntLiftedOr(a: number | null, b: number | null): number | null {
  return a == null || b == null ? null : (a | b) >>> 0;
}

export function uIntLiftedComplement(a: number | null): number | null {
  return a == null ? null : ~a >>> 0;
}

export function uIntHighBit(): number {
  return 2147483648;
}

export function uIntAllBits(): number {
  return 4294967295;
}

export function uIntFieldCompound(start: number, mask: number): number {
  const flags = new FlagSet(start);
  flags.value = (flags.value | mask) >>> 0;
  flags.value = (flags.value << 1) >>> 0;
  flags.value = (flags.value ^ mask) >>> 0;
  flags.value = flags.value >>> 3;
  flags.value = (flags.value & ~mask >>> 0) >>> 0;

  return flags.value;
}

export class FlagSet {
  value: number = 0;

  constructor(value: number) {
    this.value = value;
  }
}
