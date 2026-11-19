import type { Temporal } from "@js-temporal/polyfill";
import { durationTicks } from "./temporal-helpers.ts";

/** TimeSpan's invariant constant (c) text, including all seven fractional tick digits. */
export function formatTimeSpan(value: Temporal.Duration | null | undefined): string {
  if (value == null) {
    return "";
  }

  const ticks = durationTicks(value);
  const magnitude = ticks < 0n ? -ticks : ticks;
  const days = magnitude / 864_000_000_000n;
  const hours = ((magnitude / 36_000_000_000n) % 24n).toString().padStart(2, "0");
  const minutes = ((magnitude / 600_000_000n) % 60n).toString().padStart(2, "0");
  const seconds = ((magnitude / 10_000_000n) % 60n).toString().padStart(2, "0");
  const fraction = magnitude % 10_000_000n;
  const prefix = `${ticks < 0n ? "-" : ""}${days ? `${days}.` : ""}`;
  const suffix = fraction ? `.${fraction.toString().padStart(7, "0")}` : "";

  return `${prefix}${hours}:${minutes}:${seconds}${suffix}`;
}
