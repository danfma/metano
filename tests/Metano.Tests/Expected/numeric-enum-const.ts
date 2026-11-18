export const Color = {
  Red: 0,
  Green: 1,
  Blue: 2,
} as const;

export type Color = typeof Color[keyof typeof Color];

export class Palette {
  constructor() { }

  static fromNumber(n: number): Color {
    return n as Color;
  }

  static literal(): Color {
    return 3 as Color;
  }

  static maybe(n: number | null): Color | null {
    return n as Color | null;
  }

  static next(c: Color): Color {
    return c + 1 as Color;
  }

  static combined(c: Color): Color {
    return (c | Color.Green) as Color;
  }

  static inverted(c: Color): Color {
    return ~c as Color;
  }

  static distance(a: Color, b: Color): number {
    return (a - b) | 0;
  }

  static stepped(c: Color): Color {
    (c as number)++;
    (c as number) += 1;

    return c;
  }

  static localStep(): boolean {
    let s = Color.Red as Color;
    (s as number)++;

    return s === Color.Green;
  }

  static default(): Color {
    return Color.Red;
  }

  static isBlue(c: Color): boolean {
    return c === Color.Blue;
  }

  static label(c: Color): string {
    return c === Color.Red ? "red" : c === Color.Green ? "green" : "other";
  }
}
