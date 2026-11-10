import { UUID } from "metano-runtime";
import { firstOrDefault, last, lastOrDefault, linq, select, singleOrDefault, where } from "metano-runtime/system/linq";
import { Decimal } from "decimal.js";
import { Color } from "./color";
import type { Level } from "./level";
import type { Person } from "./person";
import { Point } from "./point";
import type { Profile } from "./profile";

export class Lookups {
  constructor() { }

  first(xs: number[]): number {
    return linq(xs, firstOrDefault(undefined, 0));
  }

  firstMatch(xs: number[]): number {
    return linq(xs, firstOrDefault((x: number) => x > 100, 0));
  }

  firstOr(xs: number[]): number {
    return linq(xs, firstOrDefault(undefined, 42));
  }

  firstMatchOr(xs: number[]): number {
    return linq(xs, firstOrDefault((x: number) => x > 100, 42));
  }

  firstMatchOrDefault(xs: number[]): number {
    return linq(xs, firstOrDefault((x: number) => x > 100, 0));
  }

  firstOrNamedDefault(xs: boolean[]): boolean {
    return linq(xs, firstOrDefault(undefined, false));
  }

  falsyMatch(xs: number[]): number {
    return linq(xs, firstOrDefault((x: number) => x === 0, 42));
  }

  last(xs: number[]): number {
    return linq(xs, lastOrDefault(undefined, 0));
  }

  lastMatchOr(xs: Iterable<number>): number {
    return linq(xs, lastOrDefault((x: number) => x > 100, 42));
  }

  lastNamed(xs: number[]): number {
    return linq(xs, lastOrDefault((x: number) => x > 100, 42));
  }

  single(xs: number[]): number {
    return linq(xs, singleOrDefault((x: number) => x > 100, 0));
  }

  singleOr(xs: number[]): number {
    return linq(xs, singleOrDefault(undefined, 42));
  }

  chained(xs: number[]): number {
    return linq(xs, where((x: number) => x > 1), select((x: number) => x * 2), firstOrDefault(undefined, 0));
  }

  wide(xs: bigint[]): bigint {
    return BigInt.asIntN(64, linq(xs, firstOrDefault((x: bigint) => x > 100n, 0n)) + linq(xs, last()));
  }

  unsigned(xs: bigint[]): bigint {
    return BigInt.asUintN(64, linq(xs, firstOrDefault(undefined, 0n)) + 3n);
  }

  native(xs: bigint[]): bigint {
    return linq(xs, firstOrDefault(undefined, 0n));
  }

  nativeUnsigned(xs: bigint[]): bigint {
    return linq(xs, lastOrDefault(undefined, 0n));
  }

  big(xs: bigint[]): bigint {
    return linq(xs, singleOrDefault(undefined, 0n));
  }

  flag(xs: boolean[]): boolean {
    return linq(xs, firstOrDefault(undefined, false));
  }

  ratio(xs: number[]): number {
    return linq(xs, firstOrDefault(undefined, 0));
  }

  price(xs: Decimal[]): Decimal {
    return linq(xs, firstOrDefault(undefined, new Decimal("0")));
  }

  letter(xs: string[]): string {
    return linq(xs, firstOrDefault(undefined, "\u0000"));
  }

  id(xs: UUID[]): UUID {
    return linq(xs, firstOrDefault(undefined, UUID.empty));
  }

  zero(xs: Color[]): Color {
    return linq(xs, firstOrDefault(undefined, Color.None));
  }

  noZero(xs: Level[]): Level {
    return linq(xs, firstOrDefault(undefined, 0 as Level));
  }

  pointOr(xs: Point[]): Point {
    return linq(xs, firstOrDefault(undefined, new Point(1, 2)));
  }

  maybe(xs: (number | null)[]): number | null {
    return linq(xs, firstOrDefault());
  }

  maybeOr(xs: (number | null)[]): number | null {
    return linq(xs, firstOrDefault(undefined, 42));
  }

  name(xs: string[]): string | null {
    return linq(xs, firstOrDefault());
  }

  nameOr(xs: string[]): string {
    return linq(xs, firstOrDefault(undefined, "none"));
  }

  nameOrFallback(xs: string[], fallback: string | null): string | null {
    return linq(xs, firstOrDefault(undefined, fallback));
  }

  lastOrFallback(xs: string[], fallback: string | null): string | null {
    return linq(xs, lastOrDefault((x: string) => x !== "a", fallback));
  }

  nickOr(xs: string[], profile: Profile): string | null {
    return linq(xs, lastOrDefault((x: string) => x !== "a", profile.nick));
  }

  parenthesizedDefault(xs: number[]): number {
    return linq(xs, firstOrDefault(undefined, 0));
  }

  forgivenDefault(xs: number[]): number {
    return linq(xs, lastOrDefault((x: number) => x > 1, 0));
  }

  who(xs: Person[]): Person | null {
    return linq(xs, lastOrDefault((p: Person) => p.name === "ana"));
  }

  any<T>(xs: T[]): T | null {
    return linq(xs, firstOrDefault());
  }
}
