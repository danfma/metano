import { extCompute as extCompute$1 } from "ext-lib";
import extDefault$1 from "ext-def";

export class UsesExt {
  declare private _x: number;
  declare private _y: number;

  constructor(extCompute: () => number, extDefault: number) {
    this._x = extCompute$1();
    this._y = extDefault$1();
  }
}
