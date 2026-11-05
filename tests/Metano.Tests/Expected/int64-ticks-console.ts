import { Temporal } from "@js-temporal/polyfill";
import { durationFromTicks, durationTicks, plainDateTimeFromTicks, plainDateTimeTicks } from "metano-runtime";

export class Clock {
  constructor() { }

  ticks(span: Temporal.Duration, moment: Temporal.PlainDateTime): bigint {
    return BigInt.asIntN(64, BigInt.asIntN(64, durationTicks(span) + plainDateTimeTicks(moment)) + 10000000n);
  }

  span(ticks: bigint): Temporal.Duration {
    return durationFromTicks(ticks);
  }

  constructed(ticks: bigint): Temporal.Duration {
    return durationFromTicks(ticks);
  }

  moment(ticks: bigint): Temporal.PlainDateTime {
    return plainDateTimeFromTicks(ticks);
  }

  parts(): Temporal.PlainDateTime {
    return new Temporal.PlainDateTime(2026, 1, 1);
  }

  print(l: bigint, u: bigint, i: number): void {
    console.log(String(l));
    console.log(String(u));
    console.log(i);
  }
}
