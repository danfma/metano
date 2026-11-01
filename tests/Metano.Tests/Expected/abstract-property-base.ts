export abstract class Shape {
  abstract readonly kind: string;

  constructor() { }

  describe(): string {
    return "shape:" + this.kind;
  }
}
