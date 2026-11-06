/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Temporal } from "@js-temporal/polyfill";
import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";

export class Schedule {
  constructor(
    readonly at: Temporal.PlainDateTime,
    readonly starts: Temporal.PlainTime,
    readonly day: Temporal.PlainDate,
    readonly duration: Temporal.Duration,
    readonly moment: Temporal.ZonedDateTime,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      valueEquals(this.at, other.at) &&
      valueEquals(this.starts, other.starts) &&
      valueEquals(this.day, other.day) &&
      valueEquals(this.duration, other.duration) &&
      valueEquals(this.moment, other.moment)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.at);
    hc.add(this.starts);
    hc.add(this.day);
    hc.add(this.duration);
    hc.add(this.moment);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Schedule>): Schedule {
    return copyRecordWith<Schedule>(this, overrides);
  }
}
