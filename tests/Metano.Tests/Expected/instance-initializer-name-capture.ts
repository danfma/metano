import { compute, compute as compute$1 } from "./helpers.js";

export class Shadow {
  declare private _x: number;
  declare y: number;

  constructor(compute: number) {
    this._x = compute$1();
    this.y = compute;
  }
}
import { compute, compute as compute$1 } from "./helpers.js";

export class LocalShadow {
  declare private _x: number;
  declare y: number;

  constructor() {
    this._x = compute$1();
    this.y = 0;
    const compute = 3;
    this.y = (compute + this._x) | 0;
  }
}
