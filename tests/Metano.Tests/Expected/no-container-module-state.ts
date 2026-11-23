import { Decimal } from "metano-runtime/system/decimal";

export const Min: number = 1;
export const max: number = 10;
export let hits: number = 5;
export let total: Decimal = /* @__PURE__ */ new Decimal("0");
export let label: string | null = null;
export const step: number = 2;
export let enabled: boolean = false;
const secret: number = 7;
export const ceiling: number = 100;

export function hit(): void {
  hits = (hits + 1) | 0;
  hits = (hits + step) | 0;
  total = total.plus(new Decimal("1"));
}

export function clamp(v: number): number {
  return v > max ? max : v < Min ? Min : (v + secret + ceiling) | 0;
}
