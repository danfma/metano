import { compute } from "./helpers.js";

export class Shadow {
  declare private _x: number;
  declare y: number;

  constructor(compute$: number) {
    this._x = compute();
    this.y = compute$;
  }
}
import { compute } from "./helpers.js";

export class LocalShadow {
  declare private _x: number;
  declare y: number;

  constructor() {
    this._x = compute();
    this.y = 0;
    const compute$1 = 3;
    this.y = (compute$1 + this._x) | 0;
  }
}
