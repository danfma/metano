import { checkedNarrowInt32, intDiv } from "metano-runtime";

export class Small {
  field: number = 0;

  property: number = 0;

  cells: number[] = new Array<number>(4).fill(0);

  optional: number | null = null;

  constructor() { }

  add(b: number): number {
    b = (b + 1) & 0xFF;

    return b;
  }

  increment(b: number): number {
    b = (b + 1) & 0xFF;

    return b;
  }

  decrement(b: number): number {
    b = ((b - 1) << 24) >> 24;

    return b;
  }

  postfix(b: number): number {
    const old = ((b = (b + 1) & 0xFF) - 1) & 0xFF;

    return old;
  }

  multiply(s: number): number {
    s = ((s * 3) << 16) >> 16;

    return s;
  }

  divide(s: number, d: number): number {
    s = (intDiv(s, d) << 24) >> 24;

    return s;
  }

  divideUnsigned(b: number, d: number): number {
    b = intDiv(b, d);

    return b;
  }

  shiftLeft(s: number): number {
    s = (s << 4) & 0xFFFF;

    return s;
  }

  shiftRight(s: number): number {
    s >>= 4;

    return s;
  }

  shiftUnsigned(s: number): number {
    s = ((s >>> 4) << 16) >> 16;

    return s;
  }

  or(b: number, m: number): number {
    b |= m;
    b &= m;
    b ^= m;
    b %= m;

    return b;
  }

  onField(): void {
    this.field = (this.field + 200) & 0xFF;
    this.field = (this.field + 1) & 0xFF;
  }

  onProperty(): void {
    this.property = ((this.property - 1) << 16) >> 16;
    this.property = ((this.property - 1) << 16) >> 16;
  }

  onElement(i: number): void {
    this.cells[i] = (this.cells[i] + 200) & 0xFF;
    this.cells[i] = (this.cells[i] + 1) & 0xFF;
  }

  onElementOnce(i: number): void {
    ((receiver$temp$0) => this.cells[receiver$temp$0] = (this.cells[receiver$temp$0] + 1) & 0xFF)(this.next(i));
  }

  onOptional(): void {
    this.optional = this.optional == null ? null : ((this.optional + 1) << 24) >> 24;
    this.optional = this.optional == null ? null : ((this.optional + 1) << 24) >> 24;
  }

  loop(n: number): number {
    let total = 0;
    for (let i = 0; i < n; i = (i + 1) & 0xFF) {
      total += i;
    }

    return total;
  }

  checked(b: number): number {
    {
      b = checkedNarrowInt32(b + 1, 8, false);
      b = checkedNarrowInt32(b + 1, 8, false);
      b = checkedNarrowInt32(b << 1, 8, false);
      b >>= 1;
    }

    return b;
  }

  checkedField(): void {
    {
      this.field = checkedNarrowInt32(this.field - 1, 8, false);
      this.field = checkedNarrowInt32(this.field - 1, 8, false);
    }
  }

  untouched(i: number): number {
    i++;
    i += 2;
    i *= 3;

    return i;
  }

  private next(i: number): number {
    return i + 1;
  }
}
