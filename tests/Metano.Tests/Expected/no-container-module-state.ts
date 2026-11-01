import { Decimal } from "decimal.js";

export const max: number = 10;
export const min: number = 1;
export let hits: number = 5;
export let total: Decimal = new Decimal("0");
export let label: string | null = null;
export const step: number = 2;
export let enabled: boolean = false;
const secret: number = 7;
export const ceiling: number = 100;

export function hit(): void {
  hits++;
  hits += step;
  total = total.plus(new Decimal("1"));
}

export function clamp(v: number): number {
  return v > max ? max : v < min ? min : v + secret + ceiling;
}
