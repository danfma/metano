import { checkedNarrowInt32, intDiv } from "metano-runtime";

export function sByteAdd(a: number, b: number): number {
  a = ((a + b) << 24) >> 24;

  return a;
}

export function sByteSubtract(a: number, b: number): number {
  a = ((a - b) << 24) >> 24;

  return a;
}

export function sByteMultiply(a: number, b: number): number {
  a = ((a * b) << 24) >> 24;

  return a;
}

export function sByteAnd(a: number, b: number): number {
  a &= b;

  return a;
}

export function sByteOr(a: number, b: number): number {
  a |= b;

  return a;
}

export function sByteXor(a: number, b: number): number {
  a ^= b;

  return a;
}

export function sByteAddChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a + b, 8, true);
  }

  return a;
}

export function sByteSubtractChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a - b, 8, true);
  }

  return a;
}

export function sByteMultiplyChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a * b, 8, true);
  }

  return a;
}

export function sByteDivide(a: number, divisor: number): number {
  a = (intDiv(a, divisor) << 24) >> 24;

  return a;
}

export function sByteRemainder(a: number, divisor: number): number {
  a %= divisor;

  return a;
}

export function sByteDivideChecked(a: number, divisor: number): number {
  {
    a = checkedNarrowInt32(intDiv(a, divisor), 8, true);
  }

  return a;
}

export function sByteShiftLeft(a: number, count: number): number {
  a = ((a << count) << 24) >> 24;

  return a;
}

export function sByteShiftLeftChecked(a: number, count: number): number {
  {
    a = checkedNarrowInt32(a << count, 8, true);
  }

  return a;
}

export function sByteShiftRight(a: number, count: number): number {
  a >>= count;

  return a;
}

export function sByteShiftRightChecked(a: number, count: number): number {
  {
    a >>= count;
  }

  return a;
}

export function sByteShiftRightLogical(a: number, count: number): number {
  a = ((a >>> count) << 24) >> 24;

  return a;
}

export function sByteShiftRightLogicalChecked(a: number, count: number): number {
  {
    a = checkedNarrowInt32((a >>> count) | 0, 8, true);
  }

  return a;
}

export function sBytePrefixIncrement(a: number): number {
  return a = ((a + 1) << 24) >> 24;
}

export function sBytePostfixIncrement(a: number): number {
  const previous = (((a = ((a + 1) << 24) >> 24) - 1) << 24) >> 24;

  return previous;
}

export function sBytePostfixIncrementStored(a: number): number {
  a = ((a + 1) << 24) >> 24;

  return a;
}

export function sBytePrefixIncrementChecked(a: number): number {
  return a = checkedNarrowInt32(a + 1, 8, true);
}

export function sBytePostfixIncrementChecked(a: number): number {
  {
    const previous = (a = checkedNarrowInt32(a + 1, 8, true)) - 1;

    return previous;
  }
}

export function sBytePrefixDecrement(a: number): number {
  return a = ((a - 1) << 24) >> 24;
}

export function sBytePostfixDecrement(a: number): number {
  const previous = (((a = ((a - 1) << 24) >> 24) + 1) << 24) >> 24;

  return previous;
}

export function sBytePostfixDecrementStored(a: number): number {
  a = ((a - 1) << 24) >> 24;

  return a;
}

export function sBytePrefixDecrementChecked(a: number): number {
  return a = checkedNarrowInt32(a - 1, 8, true);
}

export function sBytePostfixDecrementChecked(a: number): number {
  {
    const previous = (a = checkedNarrowInt32(a - 1, 8, true)) + 1;

    return previous;
  }
}

export function byteAdd(a: number, b: number): number {
  a = (a + b) & 0xFF;

  return a;
}

export function byteSubtract(a: number, b: number): number {
  a = (a - b) & 0xFF;

  return a;
}

export function byteMultiply(a: number, b: number): number {
  a = (a * b) & 0xFF;

  return a;
}

export function byteAnd(a: number, b: number): number {
  a &= b;

  return a;
}

export function byteOr(a: number, b: number): number {
  a |= b;

  return a;
}

export function byteXor(a: number, b: number): number {
  a ^= b;

  return a;
}

export function byteAddChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a + b, 8, false);
  }

  return a;
}

export function byteSubtractChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a - b, 8, false);
  }

  return a;
}

export function byteMultiplyChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a * b, 8, false);
  }

  return a;
}

export function byteDivide(a: number, divisor: number): number {
  a = intDiv(a, divisor);

  return a;
}

export function byteRemainder(a: number, divisor: number): number {
  a %= divisor;

  return a;
}

export function byteDivideChecked(a: number, divisor: number): number {
  {
    a = intDiv(a, divisor);
  }

  return a;
}

export function byteShiftLeft(a: number, count: number): number {
  a = (a << count) & 0xFF;

  return a;
}

export function byteShiftLeftChecked(a: number, count: number): number {
  {
    a = checkedNarrowInt32(a << count, 8, false);
  }

  return a;
}

export function byteShiftRight(a: number, count: number): number {
  a >>= count;

  return a;
}

export function byteShiftRightChecked(a: number, count: number): number {
  {
    a >>= count;
  }

  return a;
}

export function byteShiftRightLogical(a: number, count: number): number {
  a >>>= count;

  return a;
}

export function byteShiftRightLogicalChecked(a: number, count: number): number {
  {
    a >>>= count;
  }

  return a;
}

export function bytePrefixIncrement(a: number): number {
  return a = (a + 1) & 0xFF;
}

export function bytePostfixIncrement(a: number): number {
  const previous = ((a = (a + 1) & 0xFF) - 1) & 0xFF;

  return previous;
}

export function bytePostfixIncrementStored(a: number): number {
  a = (a + 1) & 0xFF;

  return a;
}

export function bytePrefixIncrementChecked(a: number): number {
  return a = checkedNarrowInt32(a + 1, 8, false);
}

export function bytePostfixIncrementChecked(a: number): number {
  {
    const previous = (a = checkedNarrowInt32(a + 1, 8, false)) - 1;

    return previous;
  }
}

export function bytePrefixDecrement(a: number): number {
  return a = (a - 1) & 0xFF;
}

export function bytePostfixDecrement(a: number): number {
  const previous = ((a = (a - 1) & 0xFF) + 1) & 0xFF;

  return previous;
}

export function bytePostfixDecrementStored(a: number): number {
  a = (a - 1) & 0xFF;

  return a;
}

export function bytePrefixDecrementChecked(a: number): number {
  return a = checkedNarrowInt32(a - 1, 8, false);
}

export function bytePostfixDecrementChecked(a: number): number {
  {
    const previous = (a = checkedNarrowInt32(a - 1, 8, false)) + 1;

    return previous;
  }
}

export function shortAdd(a: number, b: number): number {
  a = ((a + b) << 16) >> 16;

  return a;
}

export function shortSubtract(a: number, b: number): number {
  a = ((a - b) << 16) >> 16;

  return a;
}

export function shortMultiply(a: number, b: number): number {
  a = ((a * b) << 16) >> 16;

  return a;
}

export function shortAnd(a: number, b: number): number {
  a &= b;

  return a;
}

export function shortOr(a: number, b: number): number {
  a |= b;

  return a;
}

export function shortXor(a: number, b: number): number {
  a ^= b;

  return a;
}

export function shortAddChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a + b, 16, true);
  }

  return a;
}

export function shortSubtractChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a - b, 16, true);
  }

  return a;
}

export function shortMultiplyChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a * b, 16, true);
  }

  return a;
}

export function shortDivide(a: number, divisor: number): number {
  a = (intDiv(a, divisor) << 16) >> 16;

  return a;
}

export function shortRemainder(a: number, divisor: number): number {
  a %= divisor;

  return a;
}

export function shortDivideChecked(a: number, divisor: number): number {
  {
    a = checkedNarrowInt32(intDiv(a, divisor), 16, true);
  }

  return a;
}

export function shortShiftLeft(a: number, count: number): number {
  a = ((a << count) << 16) >> 16;

  return a;
}

export function shortShiftLeftChecked(a: number, count: number): number {
  {
    a = checkedNarrowInt32(a << count, 16, true);
  }

  return a;
}

export function shortShiftRight(a: number, count: number): number {
  a >>= count;

  return a;
}

export function shortShiftRightChecked(a: number, count: number): number {
  {
    a >>= count;
  }

  return a;
}

export function shortShiftRightLogical(a: number, count: number): number {
  a = ((a >>> count) << 16) >> 16;

  return a;
}

export function shortShiftRightLogicalChecked(a: number, count: number): number {
  {
    a = checkedNarrowInt32((a >>> count) | 0, 16, true);
  }

  return a;
}

export function shortPrefixIncrement(a: number): number {
  return a = ((a + 1) << 16) >> 16;
}

export function shortPostfixIncrement(a: number): number {
  const previous = (((a = ((a + 1) << 16) >> 16) - 1) << 16) >> 16;

  return previous;
}

export function shortPostfixIncrementStored(a: number): number {
  a = ((a + 1) << 16) >> 16;

  return a;
}

export function shortPrefixIncrementChecked(a: number): number {
  return a = checkedNarrowInt32(a + 1, 16, true);
}

export function shortPostfixIncrementChecked(a: number): number {
  {
    const previous = (a = checkedNarrowInt32(a + 1, 16, true)) - 1;

    return previous;
  }
}

export function shortPrefixDecrement(a: number): number {
  return a = ((a - 1) << 16) >> 16;
}

export function shortPostfixDecrement(a: number): number {
  const previous = (((a = ((a - 1) << 16) >> 16) + 1) << 16) >> 16;

  return previous;
}

export function shortPostfixDecrementStored(a: number): number {
  a = ((a - 1) << 16) >> 16;

  return a;
}

export function shortPrefixDecrementChecked(a: number): number {
  return a = checkedNarrowInt32(a - 1, 16, true);
}

export function shortPostfixDecrementChecked(a: number): number {
  {
    const previous = (a = checkedNarrowInt32(a - 1, 16, true)) + 1;

    return previous;
  }
}

export function uShortAdd(a: number, b: number): number {
  a = (a + b) & 0xFFFF;

  return a;
}

export function uShortSubtract(a: number, b: number): number {
  a = (a - b) & 0xFFFF;

  return a;
}

export function uShortMultiply(a: number, b: number): number {
  a = (a * b) & 0xFFFF;

  return a;
}

export function uShortAnd(a: number, b: number): number {
  a &= b;

  return a;
}

export function uShortOr(a: number, b: number): number {
  a |= b;

  return a;
}

export function uShortXor(a: number, b: number): number {
  a ^= b;

  return a;
}

export function uShortAddChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a + b, 16, false);
  }

  return a;
}

export function uShortSubtractChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a - b, 16, false);
  }

  return a;
}

export function uShortMultiplyChecked(a: number, b: number): number {
  {
    a = checkedNarrowInt32(a * b, 16, false);
  }

  return a;
}

export function uShortDivide(a: number, divisor: number): number {
  a = intDiv(a, divisor);

  return a;
}

export function uShortRemainder(a: number, divisor: number): number {
  a %= divisor;

  return a;
}

export function uShortDivideChecked(a: number, divisor: number): number {
  {
    a = intDiv(a, divisor);
  }

  return a;
}

export function uShortShiftLeft(a: number, count: number): number {
  a = (a << count) & 0xFFFF;

  return a;
}

export function uShortShiftLeftChecked(a: number, count: number): number {
  {
    a = checkedNarrowInt32(a << count, 16, false);
  }

  return a;
}

export function uShortShiftRight(a: number, count: number): number {
  a >>= count;

  return a;
}

export function uShortShiftRightChecked(a: number, count: number): number {
  {
    a >>= count;
  }

  return a;
}

export function uShortShiftRightLogical(a: number, count: number): number {
  a >>>= count;

  return a;
}

export function uShortShiftRightLogicalChecked(a: number, count: number): number {
  {
    a >>>= count;
  }

  return a;
}

export function uShortPrefixIncrement(a: number): number {
  return a = (a + 1) & 0xFFFF;
}

export function uShortPostfixIncrement(a: number): number {
  const previous = ((a = (a + 1) & 0xFFFF) - 1) & 0xFFFF;

  return previous;
}

export function uShortPostfixIncrementStored(a: number): number {
  a = (a + 1) & 0xFFFF;

  return a;
}

export function uShortPrefixIncrementChecked(a: number): number {
  return a = checkedNarrowInt32(a + 1, 16, false);
}

export function uShortPostfixIncrementChecked(a: number): number {
  {
    const previous = (a = checkedNarrowInt32(a + 1, 16, false)) - 1;

    return previous;
  }
}

export function uShortPrefixDecrement(a: number): number {
  return a = (a - 1) & 0xFFFF;
}

export function uShortPostfixDecrement(a: number): number {
  const previous = ((a = (a - 1) & 0xFFFF) + 1) & 0xFFFF;

  return previous;
}

export function uShortPostfixDecrementStored(a: number): number {
  a = (a - 1) & 0xFFFF;

  return a;
}

export function uShortPrefixDecrementChecked(a: number): number {
  return a = checkedNarrowInt32(a - 1, 16, false);
}

export function uShortPostfixDecrementChecked(a: number): number {
  {
    const previous = (a = checkedNarrowInt32(a - 1, 16, false)) + 1;

    return previous;
  }
}

export function byteFieldCompound(start: number, amount: number): number {
  const cells = new NarrowCells(0);
  cells.byteField = start;
  cells.byteField = (cells.byteField + amount) & 0xFF;
  cells.byteField = (cells.byteField + 1) & 0xFF;

  return cells.byteField;
}

export function sByteFieldCompoundChecked(start: number, amount: number): number {
  const cells = new NarrowCells(0);
  cells.sByteField = start;

  {
    cells.sByteField = checkedNarrowInt32(cells.sByteField - amount, 8, true);
    cells.sByteField = checkedNarrowInt32(cells.sByteField - 1, 8, true);
  }

  return cells.sByteField;
}

export function shortPropertyCompound(start: number, amount: number): number {
  const cells = new NarrowCells(0);
  cells.shortProperty = start;
  cells.shortProperty = ((cells.shortProperty * amount) << 16) >> 16;
  cells.shortProperty = ((cells.shortProperty - 1) << 16) >> 16;

  return cells.shortProperty;
}

export function shortPropertyPostfix(start: number): number {
  const cells = new NarrowCells(0);
  cells.shortProperty = start;
  const previous = (((cells.shortProperty = ((cells.shortProperty + 1) << 16) >> 16) - 1) << 16) >> 16;

  return ((previous ^ cells.shortProperty) << 16) >> 16;
}

export function uShortElementCompound(start: number, amount: number): number {
  return new NarrowCells(start).addToCell(amount);
}

export function uShortElementPostfix(start: number): number {
  return new NarrowCells(start).incrementCell();
}

export function uShortElementShiftChecked(start: number, count: number): number {
  return new NarrowCells(start).shiftCellChecked(count);
}

export function byteLiftedIncrement(a: number | null): number | null {
  a = a == null ? null : (a + 1) & 0xFF;

  return a;
}

export function shortLiftedDecrementChecked(a: number | null): number | null {
  {
    a = a == null ? null : checkedNarrowInt32(a - 1, 16, true);
  }

  return a;
}

export function stepsUntilWrap(start: number): number {
  let steps = 0;
  for (let i = start; steps < 300; i = (i + 1) & 0xFF) {
    if (i === 0 && steps > 0) {
      break;
    }

    steps++;
  }

  return steps;
}

export class NarrowCells {
  private static readonly notReadOnce: number = 65535;

  private readonly _cells: number[];

  private _cellReads: number = 0;

  byteField: number = 0;

  sByteField: number = 0;

  shortProperty: number = 0;

  constructor(seed: number) {
    this._cells = [0, seed];
  }

  addToCell(amount: number): number {
    ((receiver$temp$0) => this._cells[receiver$temp$0] = (this._cells[receiver$temp$0] + amount) & 0xFFFF)(this.nextCell());

    return this._cellReads === 1 ? this._cells[1] : NarrowCells.notReadOnce;
  }

  incrementCell(): number {
    const previous = ((receiver$temp$0) => ((this._cells[receiver$temp$0] = (this._cells[receiver$temp$0] + 1) & 0xFFFF) - 1) & 0xFFFF)(this.nextCell());

    return this._cellReads === 1 ? previous ^ this._cells[1] : NarrowCells.notReadOnce;
  }

  shiftCellChecked(count: number): number {
    {
      ((receiver$temp$0) => this._cells[receiver$temp$0] = checkedNarrowInt32(this._cells[receiver$temp$0] << count, 16, false))(this.nextCell());
    }

    return this._cells[1];
  }

  private nextCell(): number {
    this._cellReads++;

    return 1;
  }
}
