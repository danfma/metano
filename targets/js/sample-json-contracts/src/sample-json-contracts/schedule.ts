/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { Temporal } from "@js-temporal/polyfill";
import {
  HashCode,
  copyRecordWith,
  formatTimeSpan,
  valueEquals,
  type ClassType,
} from "metano-runtime";

let Schedule$rt: ClassType<Schedule> | undefined;

export class Schedule {
  constructor(
    readonly at: Temporal.PlainDateTime,
    readonly starts: Temporal.PlainTime,
    readonly day: Temporal.PlainDate,
    readonly duration: Temporal.Duration,
    readonly moment: Temporal.ZonedDateTime,
  ) {}

  static get $runtimeType(): ClassType<Schedule> {
    return (Schedule$rt ??= {
      kind: "class",
      name: "Schedule",
      fullName: "SampleJsonContracts.Schedule",
      namespace: "SampleJsonContracts",
      ctor: Schedule,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Schedule => v instanceof Schedule,
    });
  }

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

  protected $printMembers(): string {
    return `At = ${this.at}, Starts = ${this.starts}, Day = ${this.day}, Duration = ${formatTimeSpan(this.duration)}, Moment = ${this.moment}`;
  }

  toString(): string {
    return `Schedule { ${this.$printMembers()} }`;
  }
}
