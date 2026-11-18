/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PointDto } from "./point-dto";

export class InlineCode {
  constructor() {}

  static now(): number {
    return performance.now() as number;
  }

  static sum(a: number, b: number): string {
    return (a + b).toFixed(2) as string;
  }

  static shout(text: string): string {
    return text.toUpperCase() as string;
  }

  static remember(value: string): void {
    (globalThis as any).__sampleRemembered = value;
  }

  static remembered(): Object | null {
    return ((globalThis as any).__sampleRemembered ?? null) as any;
  }

  static parsePoint(json: string): PointDto {
    return JSON.parse(json) as Object as unknown as PointDto;
  }

  static loose(point: PointDto): Object {
    return point as any;
  }
}
