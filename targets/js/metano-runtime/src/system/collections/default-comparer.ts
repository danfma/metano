/**
 * `EqualityComparer<T>.Default` and `Comparer<T>.Default` for the runtime
 * collections (spec 011 D18, D30; contracts/runtime-collections.md § Equality
 * and comparison).
 */
import { defaultEquals } from "../equality.ts";
import { throwNotComparable } from "./throw-helpers.ts";

/** C#'s `Comparison<T>`: negative, zero or positive as `a` sorts before, with or after `b`. */
export type Comparison<T> = (a: T, b: T) => number;

/** C#'s `IEqualityComparer<T>`; a user implementation lowers to a class with these members. */
export interface IEqualityComparer<T> {
  equals(a: T, b: T): boolean;
  getHashCode(value: T): number;
}

/** C#'s `IComparer<T>`; a user implementation lowers to a class with `compare`. */
export interface IComparer<T> {
  compare(a: T, b: T): number;
}

/**
 * What a sort or a search accepts: the `Comparison<T>` overload, the `IComparer<T>`
 * overload, or nothing for `Comparer<T>.Default`. A lowered `IComparer<T>` is always an
 * object, so `typeof argument === "function"` tells the two apart.
 */
export type ComparisonArgument<T> = Comparison<T> | IComparer<T> | null | undefined;

/**
 * `EqualityComparer<T>.Default` for collection elements and keys: the runtime's
 * `defaultEquals`, except that an array compares by reference, as .NET compares an
 * array element (a tuple-typed collection keeps `MS0001`, so an array element is a C#
 * array).
 */
export function elementEquals(a: unknown, b: unknown): boolean {
  if (Array.isArray(a) || Array.isArray(b)) {
    return a === b;
  }

  return defaultEquals(a, b);
}

/**
 * A hash consistent with {@link elementEquals}. Values that equal by value hash by
 * value: primitives; a box by its raw value; a `hashCode()` implementer through it; a
 * `Decimal` and a Temporal value (which declare `equals` but no `hashCode`) by their
 * normalized text; a `Duration` by its length; a `Date` by its time. Any other value
 * with `equals` hashes to one constant bucket, so `equals` decides. Everything else
 * hashes by identity.
 */
export function elementHashCode(value: unknown): number {
  if (value === null || value === undefined) {
    return 0;
  }

  return typeof value === "object" ? objectHashCode(value) : primitiveHashCode(value);
}

function primitiveHashCode(value: unknown): number {
  switch (typeof value) {
    case "number":
      return Number.isInteger(value) ? (value as number) | 0 : textHashCode(String(value));
    case "boolean":
      return value ? 1 : 0;
    case "function":
      return identityHash(value as object);
    case "symbol":
      return SYMBOL_BUCKET;
    default:
      return textHashCode(String(value));
  }
}

function objectHashCode(value: object): number {
  if (Array.isArray(value)) {
    return identityHash(value);
  }

  const candidate = value as {
    readonly $boxed?: unknown;
    readonly value?: unknown;
    readonly hashCode?: () => number;
  };

  if (candidate.$boxed === true) {
    return elementHashCode(candidate.value);
  }

  if (typeof candidate.hashCode === "function") {
    return candidate.hashCode();
  }

  return taggedHashCode(value, Object.prototype.toString.call(value));
}

/** Decimal, Temporal and `Date` values by what their equality compares; others by `equals` or identity. */
function taggedHashCode(value: object, tag: string): number {
  if (tag === "[object Decimal]" || isTemporalValue(tag)) {
    return textHashCode(String(value));
  }

  if (tag === "[object Temporal.Duration]") {
    return durationHashCode(value as Duration);
  }

  if (value instanceof Date) {
    return primitiveHashCode(value.getTime());
  }

  return hasEquals(value) ? EQUALS_ONLY_BUCKET : identityHash(value);
}

/** The part of a `Temporal.Duration` its hash reads. */
interface Duration {
  readonly years: number;
  readonly months: number;
  readonly weeks: number;
  total(unit: "nanoseconds"): number;
}

/** A duration's length (`PT5M` and `PT300S` are equal); one with calendar units by its text. */
function durationHashCode(duration: Duration): number {
  const calendar = duration.years !== 0 || duration.months !== 0 || duration.weeks !== 0;

  return calendar
    ? textHashCode(String(duration))
    : primitiveHashCode(duration.total("nanoseconds"));
}

function isTemporalValue(tag: string): boolean {
  return tag.startsWith("[object Temporal.") && tag !== "[object Temporal.Duration]";
}

/** A 32-bit string hash (`h * 31 + code unit`). */
function textHashCode(text: string): number {
  let hash = 0;

  for (let index = 0; index < text.length; index++) {
    hash = (Math.imul(hash, 31) + text.charCodeAt(index)) | 0;
  }

  return hash;
}

/** The bucket of a value that declares `equals` but no `hashCode`. */
const EQUALS_ONLY_BUCKET = 0x2f6b1d3;

/** Symbols compare by identity but cannot always be weak keys: one bucket, `===` decides. */
const SYMBOL_BUCKET = 0x51b0;

function hasEquals(value: object): boolean {
  return typeof (value as { readonly equals?: unknown }).equals === "function";
}

/**
 * Whether {@link elementEquals} can equal `value` to an object other than itself: the
 * keys a hash core must index by hash rather than key directly in a native `Map`.
 */
export function equalsByValue(value: unknown): boolean {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }

  const candidate = value as { readonly $boxed?: unknown; readonly hashCode?: unknown };
  const tag = Object.prototype.toString.call(value);

  return (
    hasEquals(value) ||
    candidate.$boxed === true ||
    typeof candidate.hashCode === "function" ||
    tag === "[object Temporal.Duration]" ||
    value instanceof Date
  );
}

let identities: WeakMap<object, number> | undefined;
let nextIdentity = 0;

/** A sequential number per object or function, created on first use. */
function identityHash(value: object): number {
  identities ??= new WeakMap();

  const known = identities.get(value);

  if (known !== undefined) {
    return known;
  }

  nextIdentity = (nextIdentity + 1) | 0;
  identities.set(value, nextIdentity);

  return nextIdentity;
}

/**
 * `Comparer<T>.Default.Compare`: `null` / `undefined` first; numbers as
 * `double.CompareTo` (`NaN` first, `-0` equals `0`); `bigint`, `boolean` and strings by
 * value (strings ordinally, D30); a box by its raw value; `compareTo` implementers,
 * `Decimal` (`cmp`) and Temporal values (their constructor's `compare`) through their
 * own order. Anything else throws `ArgumentException`.
 */
export function defaultCompare(a: unknown, b: unknown): number {
  if (a === b) {
    return 0;
  }

  if (a === null || a === undefined) {
    return b === null || b === undefined ? 0 : -1;
  }

  if (b === null || b === undefined) {
    return 1;
  }

  return compareValues(unboxed(a), unboxed(b));
}

function compareValues(a: unknown, b: unknown): number {
  const kind = typeof a;

  if (kind === "number" && typeof b === "number") {
    return compareNumbers(a as number, b);
  }

  if (kind !== "object") {
    return comparePrimitives(a, b);
  }

  return compareObjects(a as object, b);
}

function compareNumbers(a: number, b: number): number {
  if (Number.isNaN(a)) {
    return Number.isNaN(b) ? 0 : -1;
  }

  if (Number.isNaN(b)) {
    return 1;
  }

  return a < b ? -1 : a > b ? 1 : 0;
}

function comparePrimitives(a: unknown, b: unknown): number {
  if (typeof a !== typeof b || !isComparablePrimitive(a)) {
    throwNotComparable();
  }

  return (a as string) < (b as string) ? -1 : (a as string) > (b as string) ? 1 : 0;
}

function isComparablePrimitive(value: unknown): boolean {
  const kind = typeof value;

  return kind === "string" || kind === "bigint" || kind === "boolean";
}

function compareObjects(a: object, b: unknown): number {
  const self = a as {
    readonly compareTo?: (other: unknown) => number;
    readonly cmp?: (other: unknown) => number;
    readonly constructor?: { readonly compare?: (x: unknown, y: unknown) => number };
  };

  if (typeof self.compareTo === "function") {
    return self.compareTo(b);
  }

  if (Object.prototype.toString.call(a) === "[object Decimal]" && typeof self.cmp === "function") {
    return self.cmp(b);
  }

  const compare = self.constructor?.compare;

  if (typeof compare === "function" && isTemporal(a)) {
    return compare(a, b);
  }

  return throwNotComparable();
}

function isTemporal(value: object): boolean {
  return Object.prototype.toString.call(value).startsWith("[object Temporal.");
}

function unboxed(value: unknown): unknown {
  const box = value as { readonly $boxed?: unknown; readonly value?: unknown };

  return typeof value === "object" && box.$boxed === true ? box.value : value;
}

/** `Comparer<T>.Create(comparison)`. */
export function createComparer<T>(comparison: Comparison<T>): IComparer<T> {
  return { compare: comparison };
}

/** `EqualityComparer<T>.Default` as a value (a comparer argument). */
export const defaultEqualityComparer: IEqualityComparer<unknown> = {
  equals: elementEquals,
  getHashCode: elementHashCode,
};

/** `Comparer<T>.Default` as a value. */
export const defaultComparer: IComparer<unknown> = { compare: defaultCompare };

/**
 * `Comparer<T>.Default` typed for `T`: one comparer serves every `T` because
 * `defaultCompare` accepts any value (the one place that widens it).
 */
export function defaultComparerFor<T>(): IComparer<T> {
  return defaultComparer as IComparer<T>;
}

/**
 * `EqualityComparer<T>.Default` typed for `T`: one comparer serves every `T`
 * because `elementEquals` / `elementHashCode` accept any value.
 */
export function defaultEqualityComparerFor<T>(): IEqualityComparer<T> {
  return defaultEqualityComparer as IEqualityComparer<T>;
}

/** The comparison function a {@link ComparisonArgument} stands for. */
export function comparisonOf<T>(argument: ComparisonArgument<T>): Comparison<T> {
  if (argument === null || argument === undefined) {
    return defaultCompare;
  }

  if (typeof argument === "function") {
    return argument;
  }

  return (a, b) => argument.compare(a, b);
}
