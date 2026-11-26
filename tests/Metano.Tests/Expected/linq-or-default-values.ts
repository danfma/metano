import { UUID } from "metano-runtime";
import { List } from "metano-runtime/system/collections";
import { firstOrDefault, last, lastOrDefault, linq, select, singleOrDefault, where } from "metano-runtime/system/linq";
import { Decimal } from "metano-runtime/system/decimal";
import { Color } from "./color.js";
import type { Level } from "./level.js";
import type { Person } from "./person.js";
import { Point } from "./point.js";
import type { Profile } from "./profile.js";

export class Lookups {
  first(xs: List<number>): number {
    return linq(xs, firstOrDefault(undefined, 0));
  }

  firstMatch(xs: List<number>): number {
    return linq(xs, firstOrDefault((x: number) => x > 100, 0));
  }

  firstOr(xs: List<number>): number {
    return linq(xs, firstOrDefault(undefined, 42));
  }

  firstMatchOr(xs: List<number>): number {
    return linq(xs, firstOrDefault((x: number) => x > 100, 42));
  }

  firstMatchOrDefault(xs: List<number>): number {
    return linq(xs, firstOrDefault((x: number) => x > 100, 0));
  }

  firstOrNamedDefault(xs: List<boolean>): boolean {
    return linq(xs, firstOrDefault(undefined, false));
  }

  falsyMatch(xs: List<number>): number {
    return linq(xs, firstOrDefault((x: number) => x === 0, 42));
  }

  last(xs: number[]): number {
    return linq(xs, lastOrDefault(undefined, 0));
  }

  lastMatchOr(xs: Iterable<number>): number {
    return linq(xs, lastOrDefault((x: number) => x > 100, 42));
  }

  lastNamed(xs: List<number>): number {
    return linq(xs, lastOrDefault((x: number) => x > 100, 42));
  }

  single(xs: List<number>): number {
    return linq(xs, singleOrDefault((x: number) => x > 100, 0));
  }

  singleOr(xs: List<number>): number {
    return linq(xs, singleOrDefault(undefined, 42));
  }

  chained(xs: List<number>): number {
    return linq(xs, where((x: number) => x > 1), select((x: number) => Math.imul(x, 2)), firstOrDefault(undefined, 0));
  }

  wide(xs: List<bigint>): bigint {
    return BigInt.asIntN(64, linq(xs, firstOrDefault((x: bigint) => x > 100n, 0n)) + linq(xs, last()));
  }

  unsigned(xs: List<bigint>): bigint {
    return BigInt.asUintN(64, linq(xs, firstOrDefault(undefined, 0n)) + 3n);
  }

  native(xs: List<bigint>): bigint {
    return linq(xs, firstOrDefault(undefined, 0n));
  }

  nativeUnsigned(xs: List<bigint>): bigint {
    return linq(xs, lastOrDefault(undefined, 0n));
  }

  big(xs: List<bigint>): bigint {
    return linq(xs, singleOrDefault(undefined, 0n));
  }

  flag(xs: List<boolean>): boolean {
    return linq(xs, firstOrDefault(undefined, false));
  }

  ratio(xs: List<number>): number {
    return linq(xs, firstOrDefault(undefined, 0));
  }

  price(xs: List<Decimal>): Decimal {
    return linq(xs, firstOrDefault(undefined, new Decimal("0")));
  }

  letter(xs: List<string>): string {
    return linq(xs, firstOrDefault(undefined, "\u0000"));
  }

  id(xs: List<UUID>): UUID {
    return linq(xs, firstOrDefault(undefined, UUID.empty));
  }

  zero(xs: List<Color>): Color {
    return linq(xs, firstOrDefault(undefined, Color.None));
  }

  noZero(xs: List<Level>): Level {
    return linq(xs, firstOrDefault(undefined, 0 as Level));
  }

  pointOr(xs: List<Point>): Point {
    return linq(xs, firstOrDefault(undefined, new Point(1, 2)));
  }

  maybe(xs: List<number | null>): number | null {
    return linq(xs, firstOrDefault());
  }

  maybeOr(xs: List<number | null>): number | null {
    return linq(xs, firstOrDefault(undefined, 42));
  }

  name(xs: List<string>): string | null {
    return linq(xs, firstOrDefault());
  }

  nameOr(xs: List<string>): string {
    return linq(xs, firstOrDefault(undefined, "none"));
  }

  nameOrFallback(xs: List<string>, fallback: string | null): string | null {
    return linq(xs, firstOrDefault(undefined, fallback));
  }

  lastOrFallback(xs: List<string>, fallback: string | null): string | null {
    return linq(xs, lastOrDefault((x: string) => x !== "a", fallback));
  }

  nickOr(xs: List<string>, profile: Profile): string | null {
    return linq(xs, lastOrDefault((x: string) => x !== "a", profile.nick));
  }

  parenthesizedDefault(xs: List<number>): number {
    return linq(xs, firstOrDefault(undefined, 0));
  }

  forgivenDefault(xs: List<number>): number {
    return linq(xs, lastOrDefault((x: number) => x > 1, 0));
  }

  who(xs: List<Person>): Person | null {
    return linq(xs, lastOrDefault((p: Person) => p.name === "ana"));
  }

  any<T>(xs: List<T>): T | null {
    return linq(xs, firstOrDefault());
  }
}
