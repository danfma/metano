import { Temporal } from "@js-temporal/polyfill";
import { plainDateTime, plainDateTimeFromTicks } from "metano-runtime";

export class Stamps {
  seconds(): Temporal.PlainDateTime {
    return plainDateTime(2024, 3, 5, 10, 20, 30);
  }

  millis(): Temporal.PlainDateTime {
    return plainDateTime(2024, 3, 5, 10, 20, 30, 40);
  }

  ticks(ticks: bigint): Temporal.PlainDateTime {
    return plainDateTimeFromTicks(ticks);
  }
}
