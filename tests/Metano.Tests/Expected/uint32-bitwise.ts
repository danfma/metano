export class Bits {
  flags: number = 0;

  constructor() { }

  and(a: number, b: number): number {
    return (a & b) >>> 0;
  }

  andMask(a: number): number {
    return a & 255;
  }

  andBytes(a: number, b: number): number {
    return a & b;
  }

  or(a: number, b: number): number {
    return (a | b) >>> 0;
  }

  orBytes(a: number, b: number): number {
    return a | b;
  }

  xor(a: number, b: number): number {
    return (a ^ b) >>> 0;
  }

  not(a: number): number {
    return ~a >>> 0;
  }

  shiftLeft(a: number, n: number): number {
    return (a << n) >>> 0;
  }

  shiftRight(a: number, n: number): number {
    return a >>> n;
  }

  shiftUnsigned(a: number, n: number): number {
    return a >>> n;
  }

  highBit(): number {
    return 2147483648;
  }

  allBits(): number {
    return 4294967295;
  }

  mix(a: number, b: number): number {
    return ((a ^ b >>> 16) >>> 0) * 2246822507;
  }

  lifted(a: number | null, b: number | null): number | null {
    return a == null || b == null ? null : (a | b) >>> 0;
  }

  compound(b: number): void {
    this.flags = (this.flags | b) >>> 0;
    this.flags = (this.flags & b) >>> 0;
    this.flags = (this.flags ^ b) >>> 0;
    this.flags = (this.flags << 1) >>> 0;
    this.flags >>>= 1;
    this.flags &= 255;
  }

  intAnd(a: number, b: number): number {
    return a & b | a ^ b;
  }

  intNot(a: number): number {
    return ~a;
  }

  intShiftLeft(a: number, n: number): number {
    return a << n;
  }

  intShiftLeftConstant(b: number): number {
    return b << 8;
  }

  intCompoundShift(cells: number[], n: number): void {
    cells[0] <<= n;
  }

  shiftLeftNarrow(b: number): number {
    return b << 8;
  }

  intShiftRight(a: number, n: number): number {
    return a >> n;
  }

  intShiftUnsigned(a: number, n: number): number {
    return (a >>> n) | 0;
  }

  intShiftUnsignedMasked(a: number, n: number): number {
    return (a & 65535) >>> n;
  }

  byteShift(b: number, n: number): number {
    return b >> n;
  }
}
