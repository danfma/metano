import { HashCode, copyRecordWith, isFloat64, isInt32, valueEquals, valueHashCode } from "metano-runtime";

export class RTol {
  constructor(readonly v: number) { }

  equalsOtherTol(other: RTol, tol: number): boolean {
    return Math.abs(other.v - this.v) <= tol;
  }

  equalsByValue(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals(this.v, other.v);
  }

  equals(other: RTol, tol: number): boolean;
  equals(other: unknown): boolean;
  equals(...args: unknown[]): boolean {
    if (args.length === 1 && args[0] != null && Object.getPrototypeOf(args[0]) === Object.getPrototypeOf(this)) {
      return this.equalsByValue(args[0]);
    }

    if (args.length === 2 && args[0] instanceof RTol && isFloat64(args[1])) {
      return this.equalsOtherTol(args[0] as RTol, args[1] as number);
    }

    if (args.length === 1) {
      return this.equalsByValue(args[0]);
    }

    throw new Error("No matching overload for equals");
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.v);

    return hc.toHashCode();
  }

  with(overrides?: Partial<RTol>): RTol {
    return copyRecordWith<RTol>(this, overrides);
  }
}

export class QWith {
  constructor(readonly a: number, readonly b: string) { }

  withA(a: number): QWith {
    return new QWith(a, this.b);
  }

  withByValue(overrides?: Partial<QWith>): QWith {
    return copyRecordWith<QWith>(this, overrides);
  }

  with(a: number): QWith;
  with(overrides?: Partial<QWith>): QWith;
  with(...args: unknown[]): QWith {
    if (args.length === 0 || (args.length === 1 && (args[0] === undefined || Object.getPrototypeOf(args[0]) === Object.prototype))) {
      return this.withByValue(args[0] as Partial<QWith>);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.withA(args[0] as number);
    }

    if (args.length <= 1) {
      return this.withByValue(args[0] as Partial<QWith>);
    }

    throw new Error("No matching overload for with");
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.a === other.a && this.b === other.b;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.a);
    hc.add(this.b);

    return hc.toHashCode();
  }
}

export class QHashM {
  v: number = 0;

  constructor(v: number) {
    this.v = v;
  }

  hashCodeSeed(seed: number): number {
    return this.v + seed;
  }

  hashCodeByValue(): number {
    const hc = new HashCode();
    hc.add(this.v);

    return hc.toHashCode();
  }

  hashCode(seed: number): number;
  hashCode(): number;
  hashCode(...args: unknown[]): number {
    if (args.length === 0) {
      return this.hashCodeByValue();
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.hashCodeSeed(args[0] as number);
    }

    throw new Error("No matching overload for hashCode");
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.v === other.v;
  }
}

export abstract class ABase {
  constructor(readonly v: number) { }

  equalsOtherTol(other: ABase, tol: number): boolean {
    return false;
  }

  equalsByValue(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals(this.v, other.v);
  }

  equals(other: ABase, tol: number): boolean;
  equals(other: unknown): boolean;
  equals(...args: unknown[]): boolean {
    if (args.length === 1 && args[0] != null && Object.getPrototypeOf(args[0]) === Object.getPrototypeOf(this)) {
      return this.equalsByValue(args[0]);
    }

    if (args.length === 2 && args[0] instanceof ABase && isFloat64(args[1])) {
      return this.equalsOtherTol(args[0] as ABase, args[1] as number);
    }

    if (args.length === 1) {
      return this.equalsByValue(args[0]);
    }

    throw new Error("No matching overload for equals");
  }

  abstract hashCodeSeed(seed: number): number;

  hashCodeByValue(): number {
    const hc = new HashCode();
    hc.add(this.v);

    return hc.toHashCode();
  }

  hashCode(seed: number): number;
  hashCode(): number;
  hashCode(...args: unknown[]): number {
    if (args.length === 0) {
      return this.hashCodeByValue();
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.hashCodeSeed(args[0] as number);
    }

    throw new Error("No matching overload for hashCode");
  }

  with(overrides?: Partial<ABase>): ABase {
    return copyRecordWith<ABase>(this, overrides);
  }
}

export class ADer extends ABase {
  constructor(v: number, readonly k: number) {
    super(v);
  }

  override equalsOtherTol(o: ABase, t: number): boolean {
    return Math.abs(o.v - this.v) <= t;
  }

  override equalsByValue(other: any): boolean {
    return super.equalsByValue(other) && this.k === other.k;
  }

  override hashCodeSeed(s: number): number {
    return s + 1;
  }

  override hashCodeByValue(): number {
    const hc = new HashCode();
    hc.add(super.hashCodeByValue());
    hc.add(this.k);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<ADer>): ADer {
    return copyRecordWith<ADer>(this, overrides);
  }
}

export class HTop {
  constructor(readonly a: number) { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.a === other.a;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.a);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HTop>): HTop {
    return copyRecordWith<HTop>(this, overrides);
  }
}

export class HMid extends HTop {
  constructor(a: number, readonly b: number) {
    super(a);
  }

  equalsOSlack(o: HMid, slack: number): boolean {
    return Math.abs(o.b - this.b) <= slack;
  }

  equalsByValue(other: any): boolean {
    return super.equals(other) && this.b === other.b;
  }

  override equals(o: HMid, slack: number): boolean;
  override equals(other: unknown): boolean;
  override equals(...args: unknown[]): boolean {
    if (args.length === 1 && args[0] != null && Object.getPrototypeOf(args[0]) === Object.getPrototypeOf(this)) {
      return this.equalsByValue(args[0]);
    }

    if (args.length === 2 && args[0] instanceof HMid && isInt32(args[1])) {
      return this.equalsOSlack(args[0] as HMid, args[1] as number);
    }

    if (args.length === 1) {
      return this.equalsByValue(args[0]);
    }

    throw new Error("No matching overload for equals");
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.b);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<HMid>): HMid {
    return copyRecordWith<HMid>(this, overrides);
  }
}

export class HLow extends HMid {
  constructor(a: number, b: number, readonly c: number) {
    super(a, b);
  }

  override equalsByValue(other: any): boolean {
    return super.equalsByValue(other) && this.c === other.c;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.c);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<HLow>): HLow {
    return copyRecordWith<HLow>(this, overrides);
  }
}

export class UObjEq {
  x: number = 0;

  constructor(x: number) {
    this.x = x;
  }

  hashCode(): number {
    return this.x;
  }

  equalsOSlack(o: UObjEq, slack: number): boolean {
    return Math.abs(o.x - this.x) <= slack;
  }

  equalsByValue(o: Object | null): boolean {
    let u: UObjEq;

    return o instanceof UObjEq && (u = o, true) && u.x === this.x;
  }

  equals(o: UObjEq, slack: number): boolean;
  equals(o: Object | null): boolean;
  equals(...args: unknown[]): boolean {
    if (args.length === 1 && args[0] != null && Object.getPrototypeOf(args[0]) === Object.getPrototypeOf(this)) {
      return this.equalsByValue(args[0] as Object | null);
    }

    if (args.length === 2 && args[0] instanceof UObjEq && isInt32(args[1])) {
      return this.equalsOSlack(args[0] as UObjEq, args[1] as number);
    }

    if (args.length === 1) {
      return this.equalsByValue(args[0] as Object | null);
    }

    throw new Error("No matching overload for equals");
  }
}

export class UBoth {
  constructor(readonly v: number) { }

  equalsOTol(o: UBoth, tol: number): boolean {
    return Math.abs(o.v - this.v) <= tol;
  }

  equalsO(o: UBoth | null): boolean {
    return !(o == null) && o.v === this.v;
  }

  equalsByValue(other: unknown): boolean {
    return this.equalsO(other instanceof UBoth ? other : null);
  }

  equals(o: UBoth, tol: number): boolean;
  equals(o: UBoth | null): boolean;
  equals(other: unknown): boolean;
  equals(...args: unknown[]): boolean {
    if (args.length === 1 && args[0] != null && Object.getPrototypeOf(args[0]) === Object.getPrototypeOf(this)) {
      return this.equalsByValue(args[0]);
    }

    if (args.length === 2 && args[0] instanceof UBoth && isFloat64(args[1])) {
      return this.equalsOTol(args[0] as UBoth, args[1] as number);
    }

    if (args.length === 1) {
      return this.equalsByValue(args[0]);
    }

    throw new Error("No matching overload for equals");
  }

  hashCode(): number {
    return 1;
  }

  with(overrides?: Partial<UBoth>): UBoth {
    return copyRecordWith<UBoth>(this, overrides);
  }
}

export class NEq {
  constructor(readonly v: number) { }

  equalsOTol(o: NEq, tol: number): boolean {
    return Math.abs(o.v - this.v) <= tol;
  }

  equalsByValue(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals(this.v, other.v);
  }

  equals(o: NEq, tol: number): boolean;
  equals(other: unknown): boolean;
  equals(...args: unknown[]): boolean {
    if (args.length === 1 && args[0] != null && Object.getPrototypeOf(args[0]) === Object.getPrototypeOf(this)) {
      return this.equalsByValue(args[0]);
    }

    if (args.length === 2 && args[0] instanceof NEq && isFloat64(args[1])) {
      return this.equalsOTol(args[0] as NEq, args[1] as number);
    }

    if (args.length === 1) {
      return this.equalsByValue(args[0]);
    }

    throw new Error("No matching overload for equals");
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.v);

    return hc.toHashCode();
  }

  with(overrides?: Partial<NEq>): NEq {
    return copyRecordWith<NEq>(this, overrides);
  }
}

export class NHash {
  constructor(readonly v: number) { }

  hashCodeSeed(seed: number): number {
    return this.v + seed;
  }

  hashCodeByValue(): number {
    const hc = new HashCode();
    hc.add(this.v);

    return hc.toHashCode();
  }

  hashCode(seed: number): number;
  hashCode(): number;
  hashCode(...args: unknown[]): number {
    if (args.length === 0) {
      return this.hashCodeByValue();
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.hashCodeSeed(args[0] as number);
    }

    throw new Error("No matching overload for hashCode");
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.v === other.v;
  }

  with(overrides?: Partial<NHash>): NHash {
    return copyRecordWith<NHash>(this, overrides);
  }
}

export class NWith {
  constructor(readonly a: number, readonly b: number) { }

  withA(a: number): NWith {
    return new NWith(a, this.b);
  }

  withByValue(overrides?: Partial<NWith>): NWith {
    return copyRecordWith<NWith>(this, overrides);
  }

  with(a: number): NWith;
  with(overrides?: Partial<NWith>): NWith;
  with(...args: unknown[]): NWith {
    if (args.length === 0 || (args.length === 1 && (args[0] === undefined || Object.getPrototypeOf(args[0]) === Object.prototype))) {
      return this.withByValue(args[0] as Partial<NWith>);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.withA(args[0] as number);
    }

    if (args.length <= 1) {
      return this.withByValue(args[0] as Partial<NWith>);
    }

    throw new Error("No matching overload for with");
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.a === other.a && this.b === other.b;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.a);
    hc.add(this.b);

    return hc.toHashCode();
  }
}

export class SEqOnly {
  x: number = 0;

  constructor(x: number) {
    this.x = x;
  }

  equals(o: SEqOnly): boolean {
    return o.x === this.x;
  }

  hashCode(): number {
    return this.x;
  }
}

export class SEqJoin {
  x: number = 0;

  constructor(x: number) {
    this.x = x;
  }

  equalsOSlack(o: SEqJoin, slack: number): boolean {
    return Math.abs(o.x - this.x) <= slack;
  }

  equalsO(o: SEqJoin): boolean {
    return o.x === this.x;
  }

  equalsByValue(other: unknown): boolean {
    return other instanceof SEqJoin ? this.equalsO(other) : false;
  }

  equals(o: SEqJoin, slack: number): boolean;
  equals(o: SEqJoin): boolean;
  equals(other: unknown): boolean;
  equals(...args: unknown[]): boolean {
    if (args.length === 1 && args[0] != null && Object.getPrototypeOf(args[0]) === Object.getPrototypeOf(this)) {
      return this.equalsByValue(args[0]);
    }

    if (args.length === 2 && args[0] instanceof SEqJoin && isInt32(args[1])) {
      return this.equalsOSlack(args[0] as SEqJoin, args[1] as number);
    }

    if (args.length === 1) {
      return this.equalsByValue(args[0]);
    }

    throw new Error("No matching overload for equals");
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);

    return hc.toHashCode();
  }
}

export class SExpl {
  x: number = 0;

  constructor(x: number) {
    this.x = x;
  }

  equals(o: SExpl): boolean {
    return o.x === this.x;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);

    return hc.toHashCode();
  }
}

export class HZero {
  constructor(readonly v: number) { }

  hashCodeDefault(): number {
    return 99;
  }

  hashCodeByValue(): number {
    const hc = new HashCode();
    hc.add(this.v);

    return hc.toHashCode();
  }

  hashCode(): number;
  hashCode(...args: unknown[]): number {
    if (args.length === 0) {
      return this.hashCodeByValue();
    }

    throw new Error("No matching overload for hashCode");
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.v === other.v;
  }

  with(overrides?: Partial<HZero>): HZero {
    return copyRecordWith<HZero>(this, overrides);
  }
}

export class Pt {
  x: number = 0;

  constructor(x: number) {
    this.x = x;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);

    return hc.toHashCode();
  }
}

export class JoinUse {
  constructor() { }

  static near(a: RTol, b: RTol): boolean {
    return a.equalsOtherTol(b, 0.1) && a.equalsByValue(b);
  }

  static copy(a: QWith): QWith {
    return a.withByValue({ a: 5 });
  }

  static hash(a: QHashM): number {
    return a.hashCodeSeed(2) + a.hashCodeByValue();
  }

  static low(a: HLow, b: HLow): boolean {
    return a.equalsByValue(b) && valueHashCode(a) === valueHashCode(b);
  }
}
