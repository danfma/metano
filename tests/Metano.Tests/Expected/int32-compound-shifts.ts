export class Cells {
  signed: number = 0;

  unsigned: number = 0;

  small: number = 0;

  slots: number[] = new Array<number>(4).fill(0);

  tracked: number = 0;

  constructor() { }

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
    h.next().slots[k++] <<= n;
  }

  indexAndCount(h: Cells, n: number): void {
    h.slots[h.nextIndex()] <<= h.nextCount(n);
  }

  property(h: Cells, n: number): void {
    h.next().tracked <<= n;
  }

  arrayReceiver(hs: Cells[]): void {
    hs[0].slots[1] <<= 2;
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
    ((receiver$temp$0) => receiver$temp$0.signed = (receiver$temp$0.signed >>> n) | 0)(h.next());
  }

  unsignedLeftShiftKeepsTheBinding(h: Cells, n: number): void {
    ((receiver$temp$0) => receiver$temp$0.unsigned = (receiver$temp$0.unsigned << n) >>> 0)(h.next());
  }

  narrowLeftShiftKeepsTheBinding(h: Cells, n: number): void {
    ((receiver$temp$0) => receiver$temp$0.small = (receiver$temp$0.small << n) & 0xFF)(h.next());
  }
}
