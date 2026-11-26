export class Cells {
  declare signed: number;
  declare unsigned: number;
  declare small: number;
  declare slots: number[];
  declare tracked: number;

  constructor() {
    this.signed = 0;
    this.unsigned = 0;
    this.small = 0;
    this.slots = new Array<number>(4).fill(0);
    this.tracked = 0;
  }

  next(): Cells {
    return this;
  }

  nextIndex(): number {
    return 1;
  }

  nextCount(n: number): number {
    return n;
  }

  countAsync(): Promise<number> {
    return Promise.resolve(1);
  }

  field(h: Cells, n: number): void {
    h.next().signed <<= n;
  }

  element(h: Cells, k: number, n: number): void {
    h.next().slots[((k = (k + 1) | 0) - 1) | 0]! <<= n;
  }

  indexAndCount(h: Cells, n: number): void {
    h.slots[h.nextIndex()]! <<= h.nextCount(n);
  }

  property(h: Cells, n: number): void {
    h.next().tracked <<= n;
  }

  arrayReceiver(hs: Cells[]): void {
    hs[0]!.slots[1]! <<= 2;
  }

  async awaited(h: Cells): Promise<void> {
    h.next().signed <<= await this.countAsync();
  }

  rightShift(h: Cells, n: number): void {
    h.next().signed >>= n;
  }

  unsignedRightShift(h: Cells, n: number): void {
    h.next().unsigned >>>= n;
  }

  unsignedField(n: number): void {
    this.unsigned >>>= n;
  }

  value(h: Cells, n: number): number {
    return h.next().signed <<= n;
  }

  logicalShiftKeepsTheBinding(h: Cells, n: number): void {
    const receiver$temp$0 = h.next();
    receiver$temp$0.signed = (receiver$temp$0.signed >>> n) | 0;
  }

  unsignedLeftShiftKeepsTheBinding(h: Cells, n: number): void {
    const receiver$temp$0 = h.next();
    receiver$temp$0.unsigned = (receiver$temp$0.unsigned << n) >>> 0;
  }

  narrowLeftShiftKeepsTheBinding(h: Cells, n: number): void {
    const receiver$temp$0 = h.next();
    receiver$temp$0.small = (receiver$temp$0.small << n) & 0xFF;
  }
}
