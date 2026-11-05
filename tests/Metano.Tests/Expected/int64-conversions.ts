import { checkedInt64, checkedNarrow, checkedUInt64, int32FromDouble, int32FromDoubleChecked, int64FromBigInteger, int64FromDecimal, int64FromDouble, int64FromDoubleChecked, int64ToSingle, mapNullable, uint32FromDouble, uint64FromBigInteger, uint64FromDecimal, uint64FromDouble, uint64FromDoubleChecked } from "metano-runtime";
import { Decimal } from "decimal.js";
import type { Big } from "./big";
import type { Color } from "./color";

export class Conv {
  static readonly huge: bigint = 9007199254740993n;

  static readonly uMax: bigint = 18446744073709551615n;

  static readonly small: number = 5;

  field: bigint = 0n;

  uField: bigint = 0n;

  nField: bigint | null = null;

  initialized: bigint = 7n;

  constructor() { }

  take(v: bigint): void {
  }

  widen(i: number): bigint {
    return BigInt(i);
  }

  widenU(ui: number): bigint {
    return BigInt(ui);
  }

  widenC(c: string): bigint {
    return BigInt(c.charCodeAt(0));
  }

  widenS(s: number): bigint {
    return BigInt(s);
  }

  widenB(b: number): bigint {
    return BigInt(b);
  }

  arg(i: number): bigint {
    this.take(BigInt(i));

    return 0n;
  }

  cond(f: boolean, i: number, l: bigint): bigint {
    return f ? BigInt(i) : l;
  }

  listInit(i: number): bigint[] {
    return ((receiver$temp$0) => {
      receiver$temp$0.push(1n);
      receiver$temp$0.push(BigInt(i));

      return receiver$temp$0;
    })([] as Array<bigint>);
  }

  arrInit(i: number): bigint[] {
    return [1n, BigInt(i)];
  }

  lambda(i: number): () => bigint {
    return () => BigInt(i);
  }

  compound(l: bigint, i: number): bigint {
    l += BigInt(i);
    this.field += BigInt(i);

    return l;
  }

  add(l: bigint, i: number): bigint {
    return l + BigInt(i);
  }

  toInt(l: bigint): number {
    return Number(BigInt.asIntN(32, l));
  }

  toUInt(l: bigint): number {
    return Number(BigInt.asUintN(32, l));
  }

  toShort(l: bigint): number {
    return Number(BigInt.asIntN(16, l));
  }

  toUShort(l: bigint): number {
    return Number(BigInt.asUintN(16, l));
  }

  toSByte(l: bigint): number {
    return Number(BigInt.asIntN(8, l));
  }

  toByte(l: bigint): number {
    return Number(BigInt.asUintN(8, l));
  }

  toChar(l: bigint): string {
    return String.fromCharCode(Number(BigInt.asUintN(16, l)));
  }

  toIntChecked(l: bigint): number {
    return Number(checkedNarrow(l, 32, true));
  }

  toCharChecked(l: bigint): string {
    return String.fromCharCode(Number(checkedNarrow(l, 16, false)));
  }

  toULong(l: bigint): bigint {
    return BigInt.asUintN(64, l);
  }

  fromULong(u: bigint): bigint {
    return BigInt.asIntN(64, u);
  }

  toULongChecked(l: bigint): bigint {
    return checkedUInt64(l);
  }

  fromULongChecked(u: bigint): bigint {
    return checkedInt64(u);
  }

  toDouble(l: bigint): number {
    return Number(l);
  }

  toDoubleCast(l: bigint): number {
    return Number(l);
  }

  toSingle(l: bigint): number {
    return int64ToSingle(l);
  }

  toSingleImplicit(l: bigint): number {
    return int64ToSingle(l);
  }

  fromDouble(d: number): bigint {
    return int64FromDouble(d);
  }

  fromFloat(f: number): bigint {
    return int64FromDouble(f);
  }

  fromDoubleChecked(d: number): bigint {
    return int64FromDoubleChecked(d);
  }

  uFromDouble(d: number): bigint {
    return uint64FromDouble(d);
  }

  uFromDoubleChecked(d: number): bigint {
    return uint64FromDoubleChecked(d);
  }

  intFromDouble(d: number): number {
    return int32FromDouble(d);
  }

  uIntFromDouble(d: number): number {
    return uint32FromDouble(d);
  }

  intFromDoubleChecked(d: number): number {
    return int32FromDoubleChecked(d);
  }

  byteFromDouble(d: number): number {
    return int32FromDouble(d) & 0xFF;
  }

  sByteFromDouble(d: number): number {
    return (int32FromDouble(d) << 24) >> 24;
  }

  shortFromDouble(d: number): number {
    return (int32FromDouble(d) << 16) >> 16;
  }

  uShortFromDouble(d: number): number {
    return int32FromDouble(d) & 0xFFFF;
  }

  charFromDouble(d: number): string {
    return String.fromCharCode(int32FromDouble(d) & 0xFFFF);
  }

  byteFromDoubleChecked(d: number): number {
    return Number(checkedNarrow(BigInt(int32FromDoubleChecked(d)), 8, false));
  }

  longFromIntOfDouble(d: number): bigint {
    return BigInt(int32FromDouble(d));
  }

  constCast(): number {
    return 3;
  }

  toDecimal(l: bigint): Decimal {
    return new Decimal(l.toString());
  }

  toDecimalCast(l: bigint): Decimal {
    return new Decimal(l.toString());
  }

  fromDecimal(m: Decimal): bigint {
    return int64FromDecimal(m);
  }

  uFromDecimal(m: Decimal): bigint {
    return uint64FromDecimal(m);
  }

  toBig(l: bigint): bigint {
    return l;
  }

  fromBig(b: bigint): bigint {
    return int64FromBigInteger(b);
  }

  uFromBig(b: bigint): bigint {
    return uint64FromBigInteger(b);
  }

  fromEnum(e: Color): bigint {
    return BigInt(e);
  }

  toEnum(l: bigint): Color {
    return Number(BigInt.asIntN(32, l));
  }

  toBigEnum(l: bigint): Big {
    return Number(l);
  }

  fromBigEnum(e: Big): bigint {
    return BigInt(e);
  }

  liftId(ni: number | null): bigint | null {
    return ni == null ? null : BigInt(ni);
  }

  liftCall(): bigint | null {
    return mapNullable(this.f(), (value) => BigInt(value));
  }

  narrowLift(nl: bigint | null): number | null {
    return nl == null ? null : Number(BigInt.asIntN(32, nl));
  }

  f(): number | null {
    return 1;
  }

  eq(l: bigint, i: number): boolean {
    return l === BigInt(i);
  }

  eqConst(l: bigint): boolean {
    return l !== 5n;
  }

  rel(l: bigint, i: number, ui: number): boolean {
    return l < i && l >= ui;
  }

  relU(ui: number, i: number): boolean {
    return ui < i;
  }

  promote(ui: number, i: number): bigint {
    return BigInt(ui) + BigInt(i);
  }

  promoteNarrow(ui: number, i: number): number {
    return Number(BigInt.asIntN(32, BigInt(ui) + BigInt(i)));
  }

  pat(l: bigint): boolean {
    return l === 0n || l > 10n;
  }

  sw(l: bigint): string {
    return l === 5n ? "five" : "other";
  }

  swStmt(l: bigint): string {
    switch (l) {
      case 1099511627776n:
        return "big";
      case 3n:
        return "three";
      default:
        return "x";
    }
  }

  str(l: bigint): string {
    return "id=" + l + `${l}` + l.toString();
  }

  plus(l: bigint): bigint {
    return l;
  }

  max(): bigint {
    return 9223372036854775807n;
  }

  min(): bigint {
    return -9223372036854775808n;
  }

  uMaxV(): bigint {
    return 18446744073709551615n;
  }

  shifted(): bigint {
    return 1099511627776n;
  }

  unch(): bigint {
    return -1n;
  }

  hex(): bigint {
    return 255n;
  }

  uHex(): bigint {
    return 18446744073709551615n;
  }

  def(): bigint {
    return 0n;
  }

  defT(): bigint {
    return 0n;
  }

  opt(x: bigint = 5n): bigint {
    return x;
  }

  refConst(): bigint {
    return Conv.huge;
  }

  intConst(): bigint {
    return 5n;
  }

  nMax(): bigint {
    return 9223372036854775807n;
  }

  nMin(): bigint {
    return -9223372036854775808n;
  }

  nUMax(): bigint {
    return 18446744073709551615n;
  }

  ptrSize(): number {
    return 8;
  }

  nWiden(i: number): bigint {
    return BigInt(i);
  }

  mixedRel(l: bigint, d: number): number {
    return Number(l) < d ? 1 : 0;
  }

  uncheckedInt(): bigint {
    const x = -1n;

    return x;
  }

  checkedLoop(longs: bigint[]): number {
    let last = 0;

    {
      for (const x$element of longs) {
        const x = Number(checkedNarrow(x$element, 32, false));
        last = x;
      }
    }

    return last;
  }

  uncheckedLoop(longs: bigint[]): number {
    let last = 0;
    for (const x$element of longs) {
      const x = Number(BigInt.asUintN(32, x$element));
      last = x;
    }

    return last;
  }
}
