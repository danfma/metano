import { durationFromTicks, durationTicks, plainDateTimeFromTicks, plainDateTimeTicks } from "metano-runtime";
import { averageInt64, averageNullableInt64, count, linq, maxNullableInt64Of, maxUInt64Of, minInt64Of, minNullableInt64Of, sumInt64 } from "metano-runtime/system/linq";

export function sumOfThree(a: bigint, b: bigint, c: bigint): bigint {
  return linq([a, b, c], sumInt64());
}

export function averageOfTwo(a: bigint, b: bigint): number {
  return linq([a, b], averageInt64());
}

export function nullableSum(a: bigint | null, b: bigint | null): bigint | null {
  return linq([a, b], sumInt64());
}

export function nullableAverage(a: bigint | null, b: bigint | null): number | null {
  return linq([a, b], averageNullableInt64());
}

export function minOfTwo(a: bigint, b: bigint): bigint {
  return linq([a, b], minInt64Of());
}

export function maxOfUnsigned(a: bigint, b: bigint): bigint {
  return linq([a, b], maxUInt64Of());
}

export function nullableMax(a: bigint | null, b: bigint | null): bigint | null {
  return linq([a, b], maxNullableInt64Of());
}

export function countPositive(a: bigint, b: bigint): bigint {
  return BigInt(linq([a, b], count((x: bigint) => x > 0n)));
}

export function emptyAverage(): number {
  return linq(new Array<bigint>(0).fill(0n), averageInt64());
}

export function emptyMin(): bigint {
  return linq(new Array<bigint>(0).fill(0n), minInt64Of());
}

export function emptyNullableMin(): bigint | null {
  return linq(new Array(0).fill(null), minNullableInt64Of());
}

export function spanRoundTrip(ticks: bigint): bigint {
  return durationTicks(durationFromTicks(ticks));
}

export function constructedSpanRoundTrip(ticks: bigint): bigint {
  return durationTicks(durationFromTicks(ticks));
}

export function momentRoundTrip(ticks: bigint): bigint {
  return plainDateTimeTicks(plainDateTimeFromTicks(ticks));
}
