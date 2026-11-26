export class StrictDefaults {
  private constructor() { }

  static defaulted(): [number, string] {
    const t = [0, null!] as [number, string];

    return t;
  }

  static literal(): [number, string] {
    return [0, null!] as [number, string];
  }

  static nested(): [[number, string], boolean] {
    return [[0, null!] as [number, string], false] as [[number, string], boolean];
  }
}
