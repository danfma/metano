export abstract class Shape {
  abstract readonly kind: string;

  describe(): string {
    return "shape:" + this.kind;
  }
}
