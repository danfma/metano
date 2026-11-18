export class Clock {
  private static readonly selector: string = "#app";

  constructor() { }

  now(): number {
    return (performance.now()) as number;
  }

  config(): Object {
    return ((globalThis as any).__config) as any;
  }

  sum(a: number, b: number): string {
    return ((a + b).toFixed(2)) as string;
  }

  one(): string {
    return ((1).toString()) as string;
  }

  applied(): number {
    return (((x: number) => (x + 1) | 0)(2)) as number;
  }

  found(): string | null {
    return (document.querySelector(Clock.selector)?.id) as string | null;
  }

  braces(a: number): number {
    return ({ value: a }.value) as number;
  }

  escaped(a: number): number {
    return ([{ v: a }][0].v) as number;
  }

  member(other: Clock): string {
    return (other.one().trim()) as string;
  }
}
