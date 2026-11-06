import { Shapes } from "./shapes";

export class Use {
  constructor() { }

  ofInt(x: number): number {
    return Shapes.identityT<number>(x);
  }

  ofWord(): string {
    return Shapes.identityT<string>("w");
  }

  ofString(): string {
    return Shapes.identityString("s");
  }
}
