export const Color = {
  Red: 0,
  Green: 1,
  Blue: 2,
} as const;

export type Color = typeof Color[keyof typeof Color];

export class Palette {
  static initial: Color = 0 as Color;

  private constructor() { }

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
    return ((c + 1) | 0) as Color;
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
    c = ((c + 1) | 0) as Color;
    c = ((c + 1) | 0) as Color;

    return c;
  }

  static localStep(): boolean {
    let s = Color.Red as Color;
    s = ((s + 1) | 0) as Color;

    return s === Color.Green;
  }

  static default(): Color {
    return Color.Red;
  }

  static zero(): Color {
    return 0 as Color;
  }

  static passZero(): Color {
    return Palette.identity(0 as Color);
  }

  static identity(c: Color): Color {
    return c;
  }

  static nullableNext(n: Color | null): Color | null {
    n = n == null ? null : ((n + 1) | 0) as Color;

    return n;
  }

  static nullableOr(a: Color | null, b: Color): Color | null {
    return a == null ? null : (a | b) as Color;
  }

  static nullableInverted(n: Color | null): Color | null {
    return n == null ? null : ~n as Color;
  }

  static nullableCompound(n: Color | null): Color | null {
    n = n == null ? null : ((n + 1) | 0) as Color;

    return n;
  }

  static isBlue(c: Color): boolean {
    return c === Color.Blue;
  }

  static label(c: Color): string {
    return c === Color.Red ? "red" : c === Color.Green ? "green" : "other";
  }
}
