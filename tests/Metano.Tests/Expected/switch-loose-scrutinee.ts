export function coalesced(x: number | null): string {
  return (x ?? 0) === 0 ? "zero" : "other";
}

export function either(a: boolean, b: boolean): string {
  return (a || b) === true ? "yes" : "no";
}

export function isZero(x: number | null): boolean {
  return (x ?? 0) === 0;
}
